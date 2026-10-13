using System.Globalization;
using System.Text;
using AutoShopper.Core.Shopping.Import;
using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Tests;

public sealed class ImportTests
{
    private const char HighQualityGlyph = (char)0xE03C;

    [Theory]
    [InlineData("3x Darksteel Ore", "Darksteel Ore", 3)]
    [InlineData("3 x Darksteel Ore", "Darksteel Ore", 3)]
    [InlineData("3 × Darksteel Ore", "Darksteel Ore", 3)]
    [InlineData("Darksteel Ore x3", "Darksteel Ore", 3)]
    [InlineData("Darksteel Ore", "Darksteel Ore", 1)]
    [InlineData("Darksteel Ore\t3", "Darksteel Ore", 3)]
    [InlineData("Darksteel Ore\tmetal\t12", "Darksteel Ore", 12)]
    [InlineData("1,000x Fire Shard", "Fire Shard", 1000)]
    [InlineData("- 2x Fire Shard", "Fire Shard", 2)]
    [InlineData("  •  2x   Fire    Shard  ", "Fire Shard", 2)]
    public void TextShapesReadNameAndQuantity(string line, string name, int quantity)
    {
        var row = Assert.Single(Parse(line).Rows);

        Assert.Equal(name, row.Name);
        Assert.Equal(quantity, row.Quantity);
        Assert.Equal(ItemQuality.Any, row.Quality);
        Assert.Equal(ImportRowStatus.Unknown, row.Status);
    }

    [Theory]
    [InlineData("2 Cobalt Ingot (HQ)", ItemQuality.HighQuality, "Cobalt Ingot")]
    [InlineData("Cobalt Ingot [hq]", ItemQuality.HighQuality, "Cobalt Ingot")]
    [InlineData("Cobalt Ingot HQ", ItemQuality.HighQuality, "Cobalt Ingot")]
    [InlineData("Cobalt Ingot (NQ)", ItemQuality.NormalQuality, "Cobalt Ingot")]
    [InlineData("Cobalt Ingot hq", ItemQuality.Any, "Cobalt Ingot hq")]
    public void QualityMarkersAreRead(string line, ItemQuality quality, string name)
    {
        var row = Assert.Single(Parse(line).Rows);

        Assert.Equal(quality, row.Quality);
        Assert.Equal(name, row.Name);
    }

    [Fact]
    public void TheGameHighQualityGlyphMarksHighQuality()
    {
        var row = Assert.Single(Parse(string.Concat("4x Cobalt Ingot", HighQualityGlyph.ToString())).Rows);

        Assert.Equal(ItemQuality.HighQuality, row.Quality);
        Assert.Equal("Cobalt Ingot", row.Name);
        Assert.Equal(4, row.Quantity);
    }

    [Fact]
    public void HeadingsAndCommentsAreSkipped()
    {
        var result = Parse("Crystals:\n# a comment\n// another\n\n120x Fire Shard\r\nDarksteel Ore x3\n");

        Assert.Equal(ImportFormat.Text, result.Format);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(5, result.Rows[0].SourceLine);
        Assert.Equal(6, result.Rows[1].SourceLine);
    }

    [Theory]
    [InlineData("0x Fire Shard", ImportRowProblem.QuantityOutOfRange)]
    [InlineData("1000000x Fire Shard", ImportRowProblem.QuantityOutOfRange)]
    [InlineData("99999999999999x Fire Shard", ImportRowProblem.QuantityOutOfRange)]
    public void QuantitiesOutOfRangeAreInvalid(string line, ImportRowProblem problem)
    {
        var row = Assert.Single(Parse(line).Rows);

        Assert.Equal(ImportRowStatus.Invalid, row.Status);
        Assert.Equal(problem, row.Problem);
    }

    [Fact]
    public void LongNamesAreInvalid()
    {
        var row = Assert.Single(Parse(new string('a', ShoppingLimits.MaximumItemNameLength + 1)).Rows);

        Assert.Equal(ImportRowProblem.NameTooLong, row.Problem);
    }

    [Fact]
    public void BareLeadingNumberFallsBackToTheWholeName()
    {
        var result = Parse("4 Leaf Clover\n3 Fire Shard");

        ListImporter.Resolve(result, new FakeItems());

        Assert.Equal(FakeItems.Clover, result.Rows[0].ItemId);
        Assert.Equal(1, result.Rows[0].Quantity);
        Assert.Equal(FakeItems.FireShard, result.Rows[1].ItemId);
        Assert.Equal(3, result.Rows[1].Quantity);
    }

    [Fact]
    public void AmbiguousNamesWaitForAChoice()
    {
        var result = Parse("Twin Name\nNonsense");

        ListImporter.Resolve(result, new FakeItems());

        var ambiguous = result.Rows[0];
        Assert.Equal(ImportRowStatus.Ambiguous, ambiguous.Status);
        Assert.False(ambiguous.Choose(12345));
        Assert.True(ambiguous.Choose(FakeItems.TwinB));
        Assert.True(ambiguous.IsReady);
        Assert.Equal(ImportRowStatus.Unknown, result.Rows[1].Status);
    }

    [Fact]
    public void CsvColumnsComeInAnyOrderAndTheItemIdWins()
    {
        var result = Parse("Quantity;Name;ItemId;Mode;MaxUnitPrice;Quality;ExcessAllowance;Notes\n\"1,000\";Wrong Name;2;ReachInventory;30;hq;10;note\n");

        Assert.Equal(ImportFormat.Csv, result.Format);
        Assert.Equal(1, result.IgnoredFields);
        ListImporter.Resolve(result, new FakeItems());
        var row = Assert.Single(result.Rows);
        Assert.Equal(FakeItems.FireShard, row.ItemId);
        Assert.Equal(1000, row.Quantity);
        Assert.Equal(QuantityMode.ReachInventory, row.Mode);
        Assert.Equal(ItemQuality.HighQuality, row.Quality);
        Assert.Equal(30, row.MaxUnitPrice);
        Assert.Equal(10, row.ExcessAllowance);
    }

    [Fact]
    public void CsvQuotedFieldsKeepCommasAndQuotes()
    {
        var result = Parse("Name,Quantity\n\"Say \"\"Hi\"\", Friend\",5\n");

        var row = Assert.Single(result.Rows);
        Assert.Equal("Say \"Hi\", Friend", row.Name);
        Assert.Equal(5, row.Quantity);
    }

    [Theory]
    [InlineData("Name,Quality\nFire Shard,shiny\n", ImportRowProblem.UnknownQuality)]
    [InlineData("Name,Mode\nFire Shard,sometimes\n", ImportRowProblem.UnknownMode)]
    [InlineData("Name,Quantity\nFire Shard,1.5\n", ImportRowProblem.MalformedNumber)]
    [InlineData("ItemId,Quantity\n,5\n", ImportRowProblem.MissingItem)]
    [InlineData("Name,MaxUnitPrice\nFire Shard,1000000000\n", ImportRowProblem.UnitCapOutOfRange)]
    public void CsvRowProblemsAreNamed(string text, ImportRowProblem problem)
    {
        var row = Assert.Single(Parse(text).Rows);

        Assert.Equal(ImportRowStatus.Invalid, row.Status);
        Assert.Equal(problem, row.Problem);
    }

    [Fact]
    public void CsvWithoutAnItemColumnIsRefused()
    {
        var result = CsvListParser.Parse("Foo;Bar\n1;2\n");

        Assert.Equal(ImportError.MissingColumns, result.Error);
        Assert.Empty(result.Rows);
        Assert.Equal(ImportFormat.Text, ListImporter.Detect("Foo;Bar\n1;2\n"));
    }

    [Fact]
    public void JsonExportRoundTrips()
    {
        var list = SampleList();

        var result = Parse(ListExporter.ToJson(list, new FakeItems()));

        Assert.Equal(ImportFormat.Json, result.Format);
        Assert.Equal(list.Name, result.ListName);
        Assert.Equal(list.Lines.Count, result.Rows.Count);
        ListImporter.Resolve(result, new FakeItems());
        for (var index = 0; index < list.Lines.Count; index++)
        {
            var line = list.Lines[index];
            var row = result.Rows[index];
            Assert.True(row.IsReady);
            Assert.Equal(line.Id, row.LineId);
            Assert.Equal(line.ItemId, row.ItemId);
            Assert.Equal(line.Quality, row.Quality);
            Assert.Equal(line.Mode, row.Mode);
            Assert.Equal(line.Quantity, row.Quantity);
            Assert.Equal(line.MaxUnitPrice, row.MaxUnitPrice);
            Assert.Equal(line.ExcessAllowance, row.ExcessAllowance);
        }
    }

    [Fact]
    public void CsvExportRoundTrips()
    {
        var list = SampleList();

        var result = Parse(ListExporter.ToCsv(list, new FakeItems()));
        ListImporter.Resolve(result, new FakeItems());

        Assert.Equal(ImportFormat.Csv, result.Format);
        for (var index = 0; index < list.Lines.Count; index++)
        {
            var line = list.Lines[index];
            var row = result.Rows[index];
            Assert.Equal(line.ItemId, row.ItemId);
            Assert.Equal(line.Quality, row.Quality);
            Assert.Equal(line.Mode, row.Mode);
            Assert.Equal(line.Quantity, row.Quantity);
            Assert.Equal(line.MaxUnitPrice, row.MaxUnitPrice);
            Assert.Equal(line.ExcessAllowance, row.ExcessAllowance);
        }
    }

    [Fact]
    public void TextExportKeepsItemQuantityAndQuality()
    {
        var list = SampleList();

        var result = Parse(ListExporter.ToText(list, new FakeItems()));
        ListImporter.Resolve(result, new FakeItems());

        Assert.Equal(ImportFormat.Text, result.Format);
        for (var index = 0; index < list.Lines.Count; index++)
        {
            Assert.Equal(list.Lines[index].ItemId, result.Rows[index].ItemId);
            Assert.Equal(list.Lines[index].Quantity, result.Rows[index].Quantity);
            Assert.Equal(list.Lines[index].Quality, result.Rows[index].Quality);
        }
    }

    [Theory]
    [InlineData("{\"format\":\"Other\",\"version\":1,\"lines\":[]}", ImportError.WrongFormat)]
    [InlineData("{\"format\":\"AutoShopper.List\",\"version\":2,\"lines\":[]}", ImportError.UnsupportedVersion)]
    [InlineData("{\"format\":", ImportError.MalformedJson)]
    [InlineData("{\"format\":\"AutoShopper.List\",\"version\":1,\"lines\":[]}", ImportError.Empty)]
    [InlineData("   ", ImportError.Empty)]
    public void BadJsonIsRefused(string text, ImportError error)
    {
        var result = Parse(text);

        Assert.Equal(error, result.Error);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void TooManyRowsAreRefused()
    {
        var builder = new StringBuilder();
        for (var index = 0; index <= ShoppingLimits.MaximumImportRows; index++)
        {
            builder.Append("1x Fire Shard\n");
        }

        Assert.Equal(ImportError.TooManyRows, Parse(builder.ToString()).Error);
    }

    [Fact]
    public void TooLargeTextIsRefused()
    {
        var text = string.Concat("Fire Shard ", new string('z', ShoppingLimits.MaximumImportBytes));

        Assert.Equal(ImportError.TooLarge, Parse(text).Error);
    }

    [Fact]
    public void ByteOrderMarkIsIgnored()
    {
        var result = Parse(string.Concat(((char)0xFEFF).ToString(), "ItemId,Quantity\n2,5\n"));

        Assert.Equal(ImportFormat.Csv, result.Format);
        Assert.Equal(5, Assert.Single(result.Rows).Quantity);
    }

    private static ImportResult Parse(string text)
    {
        return ListImporter.Parse(text);
    }

    private static ShoppingList SampleList()
    {
        var list = ShoppingList.Create("Crafting, \"week 1\"", Fixtures.ObservedAt);
        list.Lines.Add(ShoppingLine.Create(FakeItems.FireShard, 120, ItemQuality.Any, QuantityMode.ReachInventory));
        var ingot = ShoppingLine.Create(FakeItems.Ingot, 3, ItemQuality.HighQuality, QuantityMode.Buy);
        ingot.MaxUnitPrice = 4_500;
        ingot.ExcessAllowance = 2;
        list.Lines.Add(ingot);
        list.Lines.Add(ShoppingLine.Create(FakeItems.Ingot, 7, ItemQuality.NormalQuality, QuantityMode.Buy));
        return list;
    }

    private sealed class FakeItems : IItemNameResolver, IItemNames
    {
        public const uint FireShard = 2;
        public const uint Ingot = 5_057;
        public const uint Clover = 777;
        public const uint TwinA = 900;
        public const uint TwinB = 901;

        private static readonly Dictionary<uint, string> names = new()
        {
            [FireShard] = "Fire Shard",
            [Ingot] = "Cobalt, \"Ingot\"",
            [Clover] = "4 Leaf Clover",
        };

        public NameResolution Resolve(string nameOrId)
        {
            if (uint.TryParse(nameOrId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId))
            {
                return names.ContainsKey(itemId) ? NameResolution.Unique(itemId) : NameResolution.None;
            }

            if (string.Equals(nameOrId, "Twin Name", StringComparison.OrdinalIgnoreCase))
            {
                return NameResolution.Ambiguous([TwinA, TwinB]);
            }

            foreach (var entry in names)
            {
                if (string.Equals(entry.Value, nameOrId, StringComparison.OrdinalIgnoreCase))
                {
                    return NameResolution.Unique(entry.Key);
                }
            }

            return NameResolution.None;
        }

        public bool TryGetName(uint itemId, out string name)
        {
            return names.TryGetValue(itemId, out name!);
        }
    }
}
