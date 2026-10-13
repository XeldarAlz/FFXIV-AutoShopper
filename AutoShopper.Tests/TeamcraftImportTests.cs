using System.Text;
using AutoShopper.Core.Shopping.Import;
using AutoShopper.Core.Shopping.Model;
using static AutoShopper.Tests.ImportFixtures;

namespace AutoShopper.Tests;

public sealed class TeamcraftImportTests
{
    [Fact]
    public void WholeListIsDetected()
    {
        Assert.Equal(ImportFormat.TeamcraftText, ListImporter.Detect(Read(TeamcraftWholeList)));
        Assert.Equal(ImportFormat.TeamcraftText, ListImporter.Detect(Read(TeamcraftCrystalsPanel)));
    }

    [Fact]
    public void WholeListHeadersAreSectionsNotItems()
    {
        var result = ListImporter.Parse(Read(TeamcraftWholeList));

        Assert.Equal(ImportFormat.TeamcraftText, result.Format);
        Assert.Equal(ImportError.None, result.Error);
        Assert.Equal(7, result.Rows.Count);
        AssertRow(result.Rows[0], 2, "Fire Shard", 14, "Crystals");
        AssertRow(result.Rows[1], 3, "Wind Shard", 3, "Crystals");
        AssertRow(result.Rows[2], 6, "Iron Ore", 12, "Gathering");
        AssertRow(result.Rows[3], 7, "Animal Skin", 2, "Gathering");
        AssertRow(result.Rows[4], 10, "Linen Yarn", 1, "Vendors");
        AssertRow(result.Rows[5], 13, "Iron Ingot", 4, "Pre crafts");
        AssertRow(result.Rows[6], 14, "Hard Leather", 2, "Pre crafts");
    }

    [Fact]
    public void PreCraftsMadeFromListedMaterialsAreNotBought()
    {
        var result = ListImporter.Parse(Read(TeamcraftWholeList));

        for (var index = 0; index < 5; index++)
        {
            Assert.NotEqual(ImportRowStatus.Invalid, result.Rows[index].Status);
        }

        Assert.Equal(ImportRowProblem.CraftedFromOtherRows, result.Rows[5].Problem);
        Assert.Equal(ImportRowProblem.CraftedFromOtherRows, result.Rows[6].Problem);
    }

    [Fact]
    public void CrystalPanelTitleHasNoColon()
    {
        var result = ListImporter.Parse(Read(TeamcraftCrystalsPanel));

        Assert.Equal(2, result.Rows.Count);
        AssertRow(result.Rows[0], 2, "Fire Shard", 14, "Crystals");
        AssertRow(result.Rows[1], 3, "Wind Shard", 3, "Crystals");
    }

    [Fact]
    public void PreCraftPanelOnItsOwnIsBought()
    {
        var result = ListImporter.Parse("Pre crafts :\n4x Iron Ingot\n2x Hard Leather\n");

        Assert.Equal(ImportFormat.TeamcraftText, result.Format);
        Assert.Equal(0, result.Count(ImportRowStatus.Invalid));
    }

    [Fact]
    public void FinalItemsGiveWayToTheirPreCrafts()
    {
        var result = ListImporter.Parse("Items :\n1x Iron Sword\n\nPre crafts :\n2x Iron Ingot\n");

        Assert.Equal(ImportRowProblem.CraftedFromOtherRows, result.Rows[0].Problem);
        Assert.NotEqual(ImportRowStatus.Invalid, result.Rows[1].Status);
    }

    [Fact]
    public void TranslatedTitlesAreRecognised()
    {
        var result = ListImporter.Parse("Kristalle :\n5x Feuerscherbe\n\nVorhergehende Handwerksschritte :\n2x Eisenbarren\n");

        Assert.Equal(ImportFormat.TeamcraftText, result.Format);
        AssertRow(result.Rows[0], 2, "Feuerscherbe", 5, "Kristalle");
        Assert.Equal(ImportRowProblem.CraftedFromOtherRows, result.Rows[1].Problem);
        Assert.Equal(ImportFormat.TeamcraftText, ListImporter.Detect("Kristalle\n5x Feuerscherbe\n"));
    }

    [Fact]
    public void PlainListsStayPlain()
    {
        Assert.Equal(ImportFormat.Text, ListImporter.Detect("Ores:\n3x Iron Ore\n"));
        Assert.Equal(ImportFormat.Text, ListImporter.Detect("Gathering :\n3x Iron Ore\nCopper Ore\n"));
        Assert.Equal(ImportFormat.Text, ListImporter.Detect("3x Iron Ore\n2x Copper Ore\n"));
        Assert.Equal(ImportFormat.Text, ListImporter.Detect("Gathering :\n"));
        Assert.Equal(ImportFormat.Csv, ListImporter.Detect("ItemId,Name,Quantity\n5057,,3\n"));
    }

    [Fact]
    public void ChosenTeamcraftFormatTreatsBareLinesAsTitles()
    {
        var result = ListImporter.Parse("Shopping\n3x Iron Ore\nCopper Ore\n2x Tin Ore\n", ImportFormat.TeamcraftText);

        Assert.Equal(2, result.Rows.Count);
        AssertRow(result.Rows[0], 2, "Iron Ore", 3, "Shopping");
        AssertRow(result.Rows[1], 4, "Tin Ore", 2, "Copper Ore");
    }

    [Fact]
    public void HeadersAloneAreEmpty()
    {
        Assert.Equal(ImportError.Empty, ListImporter.Parse("Crystals :\n\nOther :\n", ImportFormat.TeamcraftText).Error);
    }

    [Fact]
    public void TooManyLinesAreRefused()
    {
        var builder = new StringBuilder("Gathering :\n");
        for (var index = 0; index <= ShoppingLimits.MaximumImportRows; index++)
        {
            builder.Append("1x Iron Ore\n");
        }

        var result = ListImporter.Parse(builder.ToString());

        Assert.Equal(ImportFormat.TeamcraftText, result.Format);
        Assert.Equal(ImportError.TooManyRows, result.Error);
    }

    [Fact]
    public void WindowsLineEndingsParseTheSame()
    {
        var unix = ListImporter.Parse(Read(TeamcraftWholeList).Replace("\r\n", "\n"));
        var windows = ListImporter.Parse(Read(TeamcraftWholeList).Replace("\r\n", "\n").Replace("\n", "\r\n"));

        Assert.Equal(unix.Rows.Count, windows.Rows.Count);
        for (var index = 0; index < unix.Rows.Count; index++)
        {
            Assert.Equal(unix.Rows[index].Name, windows.Rows[index].Name);
            Assert.Equal(unix.Rows[index].SourceLine, windows.Rows[index].SourceLine);
        }
    }

    private static void AssertRow(ImportRow row, int sourceLine, string name, int quantity, string section)
    {
        Assert.Equal(sourceLine, row.SourceLine);
        Assert.Equal(name, row.Name);
        Assert.Equal(quantity, row.Quantity);
        Assert.Equal(section, row.Section);
    }
}
