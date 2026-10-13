using AutoShopper.Core.Recipes;
using AutoShopper.Core.Shopping.Model;
using System.Globalization;
using System.Text;

namespace AutoShopper.Core.Shopping.Import;

// Imported text is data: it only ever becomes rows for the preview, and nothing in it is run or authorised. Price caps
// it carries fill the editor and are reviewed there like any other edit.
public static class ListImporter
{
    // Spreadsheets save UTF-8 files with a byte order mark, which would hide the JSON brace or the CSV header.
    private const char ByteOrderMark = (char)0xFEFF;

    public static ImportFormat Detect(string text)
    {
        // A text this long is refused as too large before it is read, so its shape is not worth scanning.
        var scansShape = text.Length <= ShoppingLimits.MaximumImportBytes;
        var start = text.AsSpan().TrimStart();
        if (!start.IsEmpty && start[0] == '{')
        {
            return scansShape && ArtisanListParser.LooksLikeArtisan(text) ? ImportFormat.ArtisanList : ImportFormat.Json;
        }

        var position = 0;
        while (TextLines.Next(text, ref position, out var line))
        {
            if (line.IsWhiteSpace())
            {
                continue;
            }

            if (CsvListParser.LooksLikeHeader(line.Trim()))
            {
                return ImportFormat.Csv;
            }

            break;
        }

        return scansShape && TeamcraftTextParser.LooksLikeTeamcraft(text) ? ImportFormat.TeamcraftText : ImportFormat.Text;
    }

    public static ImportResult Parse(string text) => Parse(text, Detect(text.TrimStart(ByteOrderMark)));

    // Reads the text as the given format, for when the user names the source instead of leaving it to detection.
    public static ImportResult Parse(string text, ImportFormat format)
    {
        text = text.TrimStart(ByteOrderMark);
        if (string.IsNullOrWhiteSpace(text))
        {
            return ImportResult.Failed(format, ImportError.Empty);
        }

        if (Encoding.UTF8.GetByteCount(text) > ShoppingLimits.MaximumImportBytes)
        {
            return ImportResult.Failed(format, ImportError.TooLarge);
        }

        return format switch
        {
            ImportFormat.Json => JsonListParser.Parse(text),
            ImportFormat.Csv => CsvListParser.Parse(text),
            ImportFormat.ArtisanList => ArtisanListParser.Parse(text),
            ImportFormat.TeamcraftText => TeamcraftTextParser.Parse(text),
            _ => TextListParser.Parse(text),
        };
    }

    // Turns the recipes of a crafting list into items to buy. The item rows resolve by id like any other import; entries
    // that are skipped, broken or unknown to the game come back as invalid rows so the preview can say why.
    public static ImportResult Expand(ImportResult crafting, ExpansionMode mode, IRecipeBook book, IItemTraits traits)
    {
        if (crafting.Error != ImportError.None || !crafting.IsCraftingList)
        {
            return crafting;
        }

        var result = new ImportResult(crafting.Format)
        {
            ListName = crafting.ListName,
            IgnoredFields = crafting.IgnoredFields,
        };

        var crafts = crafting.Crafts;
        var orders = new RecipeOrder[crafts.Count];
        var orderCount = 0;
        for (var index = 0; index < crafts.Count; index++)
        {
            var craft = crafts[index];
            if (craft.IsValid && !craft.IsSkipped)
            {
                orders[orderCount++] = new RecipeOrder(craft.RecipeId, craft.Crafts);
            }
        }

        var expansion = RecipeExpansion.Expand(orders.AsSpan(0, orderCount), mode, book, traits);
        AddDemandRows(expansion.Demands, result.Rows);
        AddEntryRows(crafts, expansion.UnknownRecipes, result.Rows);

        if (result.Rows.Count > ShoppingLimits.MaximumImportRows)
        {
            result.Fail(ImportError.TooManyRows);
        }
        else if (result.Rows.Count == 0)
        {
            result.Fail(ImportError.Empty);
        }

        return result;
    }

    public static void Resolve(ImportResult result, IItemNameResolver resolver)
    {
        var rows = result.Rows;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.Status == ImportRowStatus.Invalid)
            {
                continue;
            }

            if (row.RequestedItemId != 0)
            {
                row.Apply(resolver.Resolve(row.RequestedItemId.ToString(CultureInfo.InvariantCulture)));
                continue;
            }

            var resolution = resolver.Resolve(row.Name);
            if (resolution.Status == ResolutionStatus.Unknown && !string.IsNullOrEmpty(row.WholeName))
            {
                var whole = resolver.Resolve(row.WholeName);
                if (whole.Status != ResolutionStatus.Unknown)
                {
                    row.UseWholeName();
                    resolution = whole;
                }
            }

            row.Apply(resolution);
        }
    }

    private static void AddDemandRows(ItemDemand[] demands, List<ImportRow> rows)
    {
        for (var index = 0; index < demands.Length; index++)
        {
            var demand = demands[index];
            var row = new ImportRow(index + 1, demand.ItemId.ToString(CultureInfo.InvariantCulture))
            {
                RequestedItemId = demand.ItemId,
            };

            if (!demand.IsMarketable)
            {
                row.Reject(ImportRowProblem.NotMarketable);
            }

            ImportFields.SetQuantity(row, NumberStatus.Valid, demand.Quantity);
            rows.Add(row);
        }
    }

    private static void AddEntryRows(List<CraftingImport> crafts, uint[] unknownRecipes, List<ImportRow> rows)
    {
        for (var index = 0; index < crafts.Count; index++)
        {
            var craft = crafts[index];
            var problem = craft.Problem;
            if (problem == ImportRowProblem.None && craft.IsSkipped)
            {
                problem = ImportRowProblem.SkippedCraft;
            }
            else if (problem == ImportRowProblem.None && Array.BinarySearch(unknownRecipes, craft.RecipeId) >= 0)
            {
                problem = ImportRowProblem.UnknownRecipe;
            }

            if (problem == ImportRowProblem.None)
            {
                continue;
            }

            var row = new ImportRow(craft.SourceLine, craft.SourceText);
            row.Reject(problem);
            rows.Add(row);
        }
    }
}
