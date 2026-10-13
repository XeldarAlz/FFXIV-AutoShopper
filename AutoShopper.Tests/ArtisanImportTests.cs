using AutoShopper.Core.Recipes;
using AutoShopper.Core.Shopping.Import;
using AutoShopper.Core.Shopping.Model;
using static AutoShopper.Tests.ImportFixtures;

namespace AutoShopper.Tests;

public sealed class ArtisanImportTests
{
    [Fact]
    public void ExportIsDetectedAsArtisan()
    {
        Assert.Equal(ImportFormat.ArtisanList, ListImporter.Detect(Read(ArtisanList)));
        Assert.Equal(ImportFormat.ArtisanList, ListImporter.Detect(Read(ArtisanLegacyList)));
    }

    [Fact]
    public void FixtureParsesIntoRecipeEntries()
    {
        var result = ListImporter.Parse(Read(ArtisanList));

        Assert.Equal(ImportFormat.ArtisanList, result.Format);
        Assert.Equal(ImportError.None, result.Error);
        Assert.Equal("Ironworks sword set", result.ListName);
        Assert.Equal(0, result.IgnoredFields);
        Assert.Empty(result.Rows);
        Assert.True(result.IsCraftingList);
        Assert.Equal(3, result.Crafts.Count);
        AssertEntry(result.Crafts[0], 1, SwordRecipe, 2, false);
        AssertEntry(result.Crafts[1], 2, IngotRecipe, 4, false);
        AssertEntry(result.Crafts[2], 3, RivetsRecipe, 1, true);
    }

    [Fact]
    public void LegacySchemaIsRefused()
    {
        var result = ListImporter.Parse(Read(ArtisanLegacyList));

        Assert.Equal(ImportFormat.ArtisanList, result.Format);
        Assert.Equal(ImportError.UnsupportedVersion, result.Error);
        Assert.False(result.IsCraftingList);
    }

    [Fact]
    public void UnknownVersionIsRefused()
    {
        var result = ListImporter.Parse("{\"Version\":3,\"Name\":\"Future\",\"Recipes\":[{\"ID\":31001,\"Quantity\":1}]}");

        Assert.Equal(ImportFormat.ArtisanList, result.Format);
        Assert.Equal(ImportError.UnsupportedVersion, result.Error);
        Assert.Empty(result.Crafts);
    }

    [Fact]
    public void NativeListIsNotMistakenForArtisan()
    {
        const string native = "{\"format\":\"AutoShopper.List\",\"version\":1,\"name\":\"Recipes\",\"lines\":[{\"itemId\":5057,\"quantity\":3}]}";

        Assert.Equal(ImportFormat.Json, ListImporter.Detect(native));
        Assert.Equal(ImportFormat.Json, ListImporter.Detect("{\"format\":\"AutoShopper.List\",\"Recipes\":[]}"));
        Assert.Equal(ImportFormat.Json, ListImporter.Detect("{\"Items\":[1,2]}"));
        Assert.Equal(3, ListImporter.Parse(native).Rows[0].Quantity);
    }

    [Fact]
    public void ObjectWithoutRecipesIsTheWrongFormat()
    {
        var result = ListImporter.Parse("{\"Name\":\"Nothing\"}", ImportFormat.ArtisanList);

        Assert.Equal(ImportError.WrongFormat, result.Error);
    }

    [Fact]
    public void MalformedExportIsReported()
    {
        var text = Read(ArtisanList);

        var result = ListImporter.Parse(text[..^20]);

        Assert.Equal(ImportFormat.ArtisanList, result.Format);
        Assert.Equal(ImportError.MalformedJson, result.Error);
    }

    [Fact]
    public void BrokenEntriesCarryTheirProblem()
    {
        const string text = "{\"Recipes\":[{\"ID\":31001,\"Quantity\":0},{\"ID\":\"sword\",\"Quantity\":1},{\"Quantity\":2},"
            + "{\"ID\":31002,\"Quantity\":1000000},{\"ID\":31002,\"Quantity\":-1},{\"ID\":31002,\"Quantity\":1,\"Custom\":true},7]}";

        var result = ListImporter.Parse(text);

        Assert.Equal(ImportError.None, result.Error);
        Assert.Equal(ImportRowProblem.QuantityOutOfRange, result.Crafts[0].Problem);
        Assert.Equal(ImportRowProblem.MalformedNumber, result.Crafts[1].Problem);
        Assert.Equal(ImportRowProblem.MissingItem, result.Crafts[2].Problem);
        Assert.Equal(ImportRowProblem.QuantityOutOfRange, result.Crafts[3].Problem);
        Assert.Equal(ImportRowProblem.QuantityOutOfRange, result.Crafts[4].Problem);
        Assert.True(result.Crafts[5].IsValid);
        Assert.Equal(ImportRowProblem.MissingItem, result.Crafts[6].Problem);
        Assert.Equal(1, result.IgnoredFields);
    }

    [Fact]
    public void TooManyEntriesAreRefused()
    {
        var result = ListImporter.Parse(ArtisanJson(ShoppingLimits.MaximumImportRows + 1, 1, 1));

        Assert.Equal(ImportError.TooManyRows, result.Error);
        Assert.Equal(ImportError.None, ListImporter.Parse(ArtisanJson(ShoppingLimits.MaximumImportRows, 1, 1)).Error);
    }

    [Fact]
    public void OversizedExportIsRefused()
    {
        var text = "{\"Name\":\"" + new string('a', ShoppingLimits.MaximumImportBytes) + "\",\"Recipes\":[]}";

        Assert.Equal(ImportError.TooLarge, ListImporter.Parse(text).Error);
        Assert.Equal(ImportError.TooLarge, ListImporter.Parse(text, ImportFormat.ArtisanList).Error);
    }

    [Fact]
    public void EmptyRecipeListIsEmpty()
    {
        Assert.Equal(ImportError.Empty, ListImporter.Parse("{\"Name\":\"Empty\",\"Recipes\":[]}").Error);
    }

    [Fact]
    public void FixtureExpandsIntoResolvableItemRows()
    {
        var book = Workshop();
        var parsed = ListImporter.Parse(Read(ArtisanList));

        var expanded = ListImporter.Expand(parsed, ExpansionMode.DirectIngredients, book, book);
        ListImporter.Resolve(expanded, new FakeItemResolver(FireShard, Ore));

        Assert.Equal(ImportError.None, expanded.Error);
        Assert.Equal("Ironworks sword set", expanded.ListName);
        Assert.Equal(new Dictionary<uint, int> { [FireShard] = 10, [Ore] = 12 }, ReadyQuantities(expanded));
        Assert.Equal(2, expanded.Count(ImportRowStatus.Resolved));

        var grip = RowFor(expanded, Grip)!;
        Assert.Equal(ImportRowStatus.Invalid, grip.Status);
        Assert.Equal(ImportRowProblem.NotMarketable, grip.Problem);
        Assert.Equal(2, grip.Quantity);

        var skipped = expanded.Rows[^1];
        Assert.Equal(3, skipped.SourceLine);
        Assert.Equal(ImportRowProblem.SkippedCraft, skipped.Problem);
    }

    [Fact]
    public void FixtureExpandsByMode()
    {
        var book = Workshop();
        var parsed = ListImporter.Parse(Read(ArtisanList));

        var finished = ListImporter.Expand(parsed, ExpansionMode.FinishedItems, book, book);
        var full = ListImporter.Expand(parsed, ExpansionMode.FullyExpanded, book, book);

        Assert.Equal(new Dictionary<uint, int> { [Sword] = 2 }, ReadyQuantities(finished));
        Assert.Equal(new Dictionary<uint, int> { [FireShard] = 10, [WindShard] = 2, [Ore] = 12, [Leather] = 2 }, ReadyQuantities(full));
        Assert.Null(RowFor(full, Grip));
        Assert.Null(RowFor(full, Ingot));
    }

    [Fact]
    public void UnknownRecipeBecomesAnInvalidRow()
    {
        var book = Workshop();
        var parsed = ListImporter.Parse("{\"Recipes\":[{\"ID\":31002,\"Quantity\":1},{\"ID\":99999,\"Quantity\":1}]}");

        var expanded = ListImporter.Expand(parsed, ExpansionMode.FinishedItems, book, book);

        Assert.Equal(new Dictionary<uint, int> { [Ingot] = 1 }, ReadyQuantities(expanded));
        var unknown = expanded.Rows[^1];
        Assert.Equal(2, unknown.SourceLine);
        Assert.Equal(ImportRowProblem.UnknownRecipe, unknown.Problem);
    }

    [Fact]
    public void ExpandedQuantityAboveTheLineLimitIsInvalid()
    {
        var book = Workshop();
        var parsed = ListImporter.Parse(ArtisanJson(1, IngotRecipe, ShoppingLimits.MaximumQuantity));

        var expanded = ListImporter.Expand(parsed, ExpansionMode.DirectIngredients, book, book);

        var ore = RowFor(expanded, Ore)!;
        Assert.Equal(ImportRowStatus.Invalid, ore.Status);
        Assert.Equal(ImportRowProblem.QuantityOutOfRange, ore.Problem);
        Assert.Equal(ShoppingLimits.MaximumQuantity, RowFor(expanded, FireShard)!.Quantity);
    }

    [Fact]
    public void ExpansionWithTooManyItemsIsRefused()
    {
        const uint firstRecipe = 100_000;
        var recipes = new CraftingRecipe[ShoppingLimits.MaximumImportRows];
        for (var index = 0; index < recipes.Length; index++)
        {
            var offset = (uint)index;
            recipes[index] = Recipe(firstRecipe + offset, 200_000 + offset, 1, (300_000 + offset * 2, 1), (300_001 + offset * 2, 1));
        }

        var book = new FakeRecipeBook(recipes, []);
        var parsed = ListImporter.Parse(ArtisanJson(recipes.Length, firstRecipe, 1));

        Assert.Equal(ImportError.None, ListImporter.Expand(parsed, ExpansionMode.FinishedItems, book, book).Error);
        Assert.Equal(ImportError.TooManyRows, ListImporter.Expand(parsed, ExpansionMode.DirectIngredients, book, book).Error);
    }

    [Fact]
    public void OnlySkippedEntriesLeaveNothingToBuy()
    {
        var book = Workshop();
        var parsed = ListImporter.Parse("{\"Recipes\":[{\"ID\":31002,\"Quantity\":1,\"ListItemOptions\":{\"Skipping\":true}}]}");

        var expanded = ListImporter.Expand(parsed, ExpansionMode.FullyExpanded, book, book);

        Assert.Equal(ImportError.None, expanded.Error);
        Assert.Single(expanded.Rows);
        Assert.Equal(0, expanded.Count(ImportRowStatus.Unknown));
    }

    [Fact]
    public void ItemListsPassThroughExpansion()
    {
        var book = Workshop();
        var parsed = ListImporter.Parse("3x Iron Ore");

        Assert.Same(parsed, ListImporter.Expand(parsed, ExpansionMode.FullyExpanded, book, book));
    }

    private static void AssertEntry(CraftingImport entry, int sourceLine, uint recipeId, int crafts, bool isSkipped)
    {
        Assert.Equal(sourceLine, entry.SourceLine);
        Assert.Equal(recipeId, entry.RecipeId);
        Assert.Equal(crafts, entry.Crafts);
        Assert.Equal(isSkipped, entry.IsSkipped);
        Assert.True(entry.IsValid);
    }
}
