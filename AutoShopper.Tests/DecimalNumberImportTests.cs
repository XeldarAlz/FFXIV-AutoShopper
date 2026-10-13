using AutoShopper.Core.Shopping.Import;

namespace AutoShopper.Tests;

// A number that could carry a decimal part must never be read as a larger whole number: the row is refused with a
// problem the import preview shows, and the user writes plain digits instead.
public sealed class DecimalNumberImportTests
{
    [Theory]
    [InlineData("1,000", 1000)]
    [InlineData("12,345", 12345)]
    [InlineData("1,234,567", 1234567)]
    [InlineData("12 500", 12500)]
    [InlineData("1'000'000", 1000000)]
    [InlineData("1_000", 1000)]
    [InlineData("007", 7)]
    [InlineData("999999", 999999)]
    public void CompleteThreeDigitGroupsReadAsWritten(string text, long expected)
    {
        Assert.Equal(NumberStatus.Valid, ImportFields.TryParseWhole(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("10,5")]
    [InlineData("2500,00")]
    [InlineData("1,5000")]
    [InlineData("1234,567")]
    [InlineData("1,00,000")]
    [InlineData("1,000,00")]
    [InlineData("10 5")]
    [InlineData("1.5")]
    [InlineData("1.500")]
    [InlineData(".5")]
    [InlineData("5.")]
    public void AGroupThatCouldBeADecimalPartIsAmbiguousAndReadsAsNothing(string text)
    {
        Assert.Equal(NumberStatus.Ambiguous, ImportFields.TryParseWhole(text, out var value));
        Assert.Equal(0, value);
    }

    [Theory]
    [InlineData(",5")]
    [InlineData("1,,000")]
    [InlineData("1,000,")]
    [InlineData("five")]
    [InlineData("12x")]
    [InlineData("-5")]
    [InlineData(",")]
    public void TextThatIsNoNumberIsMalformed(string text)
    {
        Assert.Equal(NumberStatus.Malformed, ImportFields.TryParseWhole(text, out var value));
        Assert.Equal(0, value);
    }

    [Fact]
    public void TooManyDigitsOverflowWithoutAValue()
    {
        Assert.Equal(NumberStatus.Overflow, ImportFields.TryParseWhole("1234567890123", out var value));
        Assert.Equal(0, value);
    }

    [Theory]
    [InlineData("1,000")]
    [InlineData("10,5")]
    [InlineData("1,000,000")]
    public void WhereTheCommaIsTheDecimalMarkAnyCommaIsAmbiguous(string text)
    {
        Assert.Equal(NumberStatus.Ambiguous, ImportFields.TryParseWhole(text, out _, decimalComma: true));
    }

    [Theory]
    [InlineData("1000", 1000)]
    [InlineData("1 000", 1000)]
    [InlineData("1'000", 1000)]
    public void WhereTheCommaIsTheDecimalMarkOtherGroupsStillRead(string text, long expected)
    {
        Assert.Equal(NumberStatus.Valid, ImportFields.TryParseWhole(text, out var value, decimalComma: true));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void SemicolonCsvWithDecimalCommasIsRefusedNotReadTenToAHundredTimesLarger()
    {
        var row = Assert.Single(CsvListParser.Parse("ItemId;Quantity;MaxUnitPrice\n5057;10,5;2500,00\n").Rows);

        Assert.Equal(ImportRowStatus.Invalid, row.Status);
        Assert.Equal(ImportRowProblem.AmbiguousNumber, row.Problem);
        Assert.NotEqual(105, row.Quantity);
        Assert.Equal(0, row.MaxUnitPrice);
    }

    [Theory]
    [InlineData("Name;Quantity\nFire Shard;\"1,000\"\n")]
    [InlineData("Name;Quantity\nFire Shard;1,000\n")]
    [InlineData("Name;MaxUnitPrice\nFire Shard;2,500\n")]
    [InlineData("Name;ExcessAllowance\nFire Shard;1,000\n")]
    public void SemicolonCsvRefusesACommaBeforeThreeDigitsBecauseItMayMeanOnePointZero(string text)
    {
        var row = Assert.Single(CsvListParser.Parse(text).Rows);

        Assert.Equal(ImportRowStatus.Invalid, row.Status);
        Assert.Equal(ImportRowProblem.AmbiguousNumber, row.Problem);
    }

    [Theory]
    [InlineData("Name;Quantity\nFire Shard;1000\n", 1000)]
    [InlineData("Name;Quantity\nFire Shard;1 000\n", 1000)]
    public void SemicolonCsvReadsPlainDigitsAndSpaceGroups(string text, int quantity)
    {
        var row = Assert.Single(CsvListParser.Parse(text).Rows);

        Assert.NotEqual(ImportRowStatus.Invalid, row.Status);
        Assert.Equal(quantity, row.Quantity);
    }

    [Fact]
    public void CommaCsvReadsAQuotedThousandsGroup()
    {
        var row = Assert.Single(CsvListParser.Parse("Name,Quantity\nFire Shard,\"1,000\"\n").Rows);

        Assert.Equal(1000, row.Quantity);
    }

    [Theory]
    [InlineData("Name,Quantity\nFire Shard,\"10,5\"\n")]
    [InlineData("Name\tQuantity\nFire Shard\t10,5\n")]
    [InlineData("Name\tMaxUnitPrice\nFire Shard\t2500,00\n")]
    public void OtherCsvRefusesAnIncompleteGroup(string text)
    {
        var row = Assert.Single(CsvListParser.Parse(text).Rows);

        Assert.Equal(ImportRowProblem.AmbiguousNumber, row.Problem);
    }

    [Theory]
    [InlineData("10,5 Fire Shard")]
    [InlineData("10,5x Fire Shard")]
    [InlineData("Fire Shard x2,50")]
    [InlineData("Fire Shard\t10,5")]
    [InlineData("Fire Shard\t1.5\t2500")]
    public void TextLinesRefuseAQuantityThatMayBeADecimal(string line)
    {
        var row = Assert.Single(ListImporter.Parse(line).Rows);

        Assert.Equal(ImportRowStatus.Invalid, row.Status);
        Assert.Equal(ImportRowProblem.AmbiguousNumber, row.Problem);
    }

    [Fact]
    public void JsonStringNumbersFollowTheSameRule()
    {
        const string json = "{\"format\":\"AutoShopper.List\",\"version\":1,\"lines\":[{\"itemId\":5057,\"quantity\":\"10,5\"},{\"itemId\":5057,\"quantity\":\"1,000\"}]}";

        var result = ListImporter.Parse(json);

        Assert.Equal(ImportRowProblem.AmbiguousNumber, result.Rows[0].Problem);
        Assert.Equal(1000, result.Rows[1].Quantity);
    }

    [Fact]
    public void ArtisanCraftCountThatMayBeADecimalIsRefused()
    {
        var result = ListImporter.Parse("{\"Recipes\":[{\"ID\":31002,\"Quantity\":\"10,5\"}]}");

        var craft = Assert.Single(result.Crafts);
        Assert.Equal(ImportRowProblem.AmbiguousNumber, craft.Problem);
        Assert.Equal(0, craft.Crafts);
    }
}
