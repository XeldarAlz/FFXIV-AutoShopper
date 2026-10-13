using AutoShopper.Core.Shopping.Import;
using static AutoShopper.Tests.ImportFixtures;

namespace AutoShopper.Tests;

public sealed class ImportPreviewTests
{
    [Fact]
    public void TeamcraftPanelTitlesHeadTheirRows()
    {
        var result = ListImporter.Parse(Read(TeamcraftWholeList));

        var entries = ImportPreview.Entries(result.Rows);

        Assert.Equal(result.Rows.Count + 4, entries.Length);
        AssertHeader(entries[0], "Crystals", 0);
        AssertRow(entries[1], 0);
        AssertRow(entries[2], 1);
        AssertHeader(entries[3], "Gathering", 2);
        AssertRow(entries[4], 2);
        AssertRow(entries[5], 3);
        AssertHeader(entries[6], "Vendors", 4);
        AssertRow(entries[7], 4);
        AssertHeader(entries[8], "Pre crafts", 5);
        AssertRow(entries[9], 5);
        AssertRow(entries[10], 6);
    }

    [Fact]
    public void PlainTextHasNoHeaders()
    {
        var result = ListImporter.Parse("3x Darksteel Ore\n2 Iron Ore\nFire Shard");

        var entries = ImportPreview.Entries(result.Rows);

        Assert.Equal(3, entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            AssertRow(entries[index], index);
        }
    }

    [Fact]
    public void ASectionThatComesBackGetsItsTitleAgain()
    {
        var rows = new List<ImportRow>
        {
            Row(1, null),
            Row(2, "Crystals"),
            Row(3, "Crystals"),
            Row(4, null),
            Row(5, "Crystals"),
            Row(6, "Gathering"),
        };

        var entries = ImportPreview.Entries(rows);

        Assert.Equal(9, entries.Length);
        AssertRow(entries[0], 0);
        AssertHeader(entries[1], "Crystals", 1);
        AssertRow(entries[2], 1);
        AssertRow(entries[3], 2);
        AssertRow(entries[4], 3);
        AssertHeader(entries[5], "Crystals", 4);
        AssertRow(entries[6], 4);
        AssertHeader(entries[7], "Gathering", 5);
        AssertRow(entries[8], 5);
    }

    [Fact]
    public void NoRowsGiveNoEntries()
    {
        Assert.Empty(ImportPreview.Entries([]));
    }

    private static ImportRow Row(int line, string? section) => new(line, "1x Fire Shard") { Section = section };

    private static void AssertHeader(ImportPreviewEntry entry, string title, int rowIndex)
    {
        Assert.True(entry.IsHeader);
        Assert.Equal(title, entry.Header);
        Assert.Equal(rowIndex, entry.RowIndex);
    }

    private static void AssertRow(ImportPreviewEntry entry, int rowIndex)
    {
        Assert.False(entry.IsHeader);
        Assert.Equal(rowIndex, entry.RowIndex);
    }
}
