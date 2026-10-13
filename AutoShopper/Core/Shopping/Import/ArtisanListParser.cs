using AutoShopper.Core.Shopping.Model;
using System.Text;
using System.Text.Json;

namespace AutoShopper.Core.Shopping.Import;

internal static class ArtisanListParser
{
    private static readonly JsonReaderOptions readerOptions = new()
    {
        MaxDepth = JsonListParser.DocumentOptions.MaxDepth,
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Reads top-level property names only and stops at the first one that decides: the native format names itself, and
    // an Artisan list carries recipes, the per-craft list built from them, or the options of the older schema.
    public static bool LooksLikeArtisan(string text)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), readerOptions);
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                if (ImportFields.Is(name, NativeListFormat.FormatProperty))
                {
                    return false;
                }

                if (ImportFields.Is(name, ArtisanListFormat.RecipesProperty)
                    || ImportFields.Is(name, ArtisanListFormat.ExpandedListProperty)
                    || ImportFields.Is(name, ArtisanListFormat.OptionsProperty))
                {
                    return true;
                }

                reader.Skip();
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    public static ImportResult Parse(string text)
    {
        var result = new ImportResult(ImportFormat.ArtisanList);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, JsonListParser.DocumentOptions);
        }
        catch (JsonException)
        {
            result.Fail(ImportError.MalformedJson);
            return result;
        }

        using (document)
        {
            ReadList(document.RootElement, result);
        }

        return result;
    }

    private static void ReadList(JsonElement root, ImportResult result)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            result.Fail(ImportError.WrongFormat);
            return;
        }

        JsonElement recipes = default;
        var hasLegacyShape = false;
        var hasVersion = false;
        foreach (var property in root.EnumerateObject())
        {
            var value = property.Value;
            if (JsonListParser.Is(property, ArtisanListFormat.RecipesProperty))
            {
                recipes = value;
            }
            else if (JsonListParser.Is(property, ArtisanListFormat.NameProperty))
            {
                result.ListName = value.ValueKind == JsonValueKind.String
                    ? ShoppingLimits.CleanName(value.GetString(), ShoppingLimits.MaximumListNameLength)
                    : null;
            }
            else if (JsonListParser.Is(property, ArtisanListFormat.LegacyItemsProperty) || JsonListParser.Is(property, ArtisanListFormat.OptionsProperty))
            {
                hasLegacyShape = true;
            }
            else if (IsAny(property, ArtisanListFormat.VersionProperties))
            {
                hasVersion = true;
            }
            else if (!IsAny(property, ArtisanListFormat.SettingsProperties))
            {
                result.IgnoredFields++;
            }
        }

        if (hasVersion || (hasLegacyShape && recipes.ValueKind == JsonValueKind.Undefined))
        {
            result.Fail(ImportError.UnsupportedVersion);
            return;
        }

        if (recipes.ValueKind != JsonValueKind.Array)
        {
            result.Fail(ImportError.WrongFormat);
            return;
        }

        if (recipes.GetArrayLength() > ShoppingLimits.MaximumImportRows)
        {
            result.Fail(ImportError.TooManyRows);
            return;
        }

        var entryNumber = 0;
        foreach (var element in recipes.EnumerateArray())
        {
            entryNumber++;
            result.Crafts.Add(ReadEntry(entryNumber, element, result));
        }

        if (result.Crafts.Count == 0)
        {
            result.Fail(ImportError.Empty);
        }
    }

    private static CraftingImport ReadEntry(int entryNumber, JsonElement element, ImportResult result)
    {
        var sourceText = TextLines.Clip(element.GetRawText());
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new CraftingImport(entryNumber, sourceText, 0, 0, false, ImportRowProblem.MissingItem);
        }

        uint recipeId = 0;
        long crafts = 0;
        var isSkipped = false;
        var problem = ImportRowProblem.None;
        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value;
            if (JsonListParser.Is(property, ArtisanListFormat.RecipeIdProperty))
            {
                var status = JsonListParser.NumberOf(value, out var number);
                if (status == NumberStatus.Valid && number is > 0 and <= uint.MaxValue)
                {
                    recipeId = (uint)number;
                }
                else if (status != NumberStatus.Empty)
                {
                    problem = Keep(problem, ImportRowProblem.MalformedNumber);
                }
            }
            else if (JsonListParser.Is(property, ArtisanListFormat.QuantityProperty))
            {
                var status = JsonListParser.NumberOf(value, out crafts);
                if (status == NumberStatus.Malformed)
                {
                    problem = Keep(problem, ImportRowProblem.MalformedNumber);
                }
                else if (status == NumberStatus.Overflow)
                {
                    problem = Keep(problem, ImportRowProblem.QuantityOutOfRange);
                }
            }
            else if (JsonListParser.Is(property, ArtisanListFormat.OptionsProperty))
            {
                isSkipped = ReadSkipping(value, result);
            }
            else
            {
                result.IgnoredFields++;
            }
        }

        if (recipeId == 0)
        {
            problem = Keep(problem, ImportRowProblem.MissingItem);
        }

        if (!ShoppingLimits.IsValidQuantity(crafts))
        {
            problem = Keep(problem, ImportRowProblem.QuantityOutOfRange);
        }

        var validCrafts = problem == ImportRowProblem.None ? (int)crafts : 0;
        return new CraftingImport(entryNumber, sourceText, recipeId, validCrafts, isSkipped, problem);
    }

    private static bool ReadSkipping(JsonElement options, ImportResult result)
    {
        if (options.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var isSkipped = false;
        foreach (var property in options.EnumerateObject())
        {
            if (JsonListParser.Is(property, ArtisanListFormat.SkippingProperty))
            {
                isSkipped = property.Value.ValueKind == JsonValueKind.True;
            }
            else if (!JsonListParser.Is(property, ArtisanListFormat.NormalQualityOnlyProperty))
            {
                result.IgnoredFields++;
            }
        }

        return isSkipped;
    }

    private static ImportRowProblem Keep(ImportRowProblem current, ImportRowProblem next)
        => current == ImportRowProblem.None ? next : current;

    private static bool IsAny(JsonProperty property, string[] names)
    {
        for (var index = 0; index < names.Length; index++)
        {
            if (JsonListParser.Is(property, names[index]))
            {
                return true;
            }
        }

        return false;
    }
}
