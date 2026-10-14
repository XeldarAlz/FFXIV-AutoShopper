using AutoShopper.Core.Exports;
using static AutoShopper.Tests.ExportFixtures;

namespace AutoShopper.Tests;

public sealed class ExportNamesTests
{
    [Fact]
    public void ItemNamesComeFromTheCatalog()
    {
        var names = Adapter();

        Assert.True(names.TryGetName(Ore, out var ore));
        Assert.Equal("Iron Ore", ore);
        Assert.False(names.TryGetName(99_999, out var missing));
        Assert.Equal(string.Empty, missing);
    }

    [Fact]
    public void WorldsAndDataCentersResolveById()
    {
        var names = Adapter();

        Assert.True(names.TryGetWorldName(Fixtures.Beta.WorldId, out var world));
        Assert.Equal("Beta", world);
        Assert.True(names.TryGetDataCenterName(Fixtures.SecondaryDataCenter, out var dataCenter));
        Assert.Equal("Chaos", dataCenter);
        Assert.False(names.TryGetWorldName(4_242, out var unknownWorld));
        Assert.Equal(string.Empty, unknownWorld);
        Assert.False(names.TryGetDataCenterName(4_242, out _));
    }

    [Fact]
    public void ANamelessPlaceFallsBackAndTheFirstNameOfAnIdWins()
    {
        var names = new ExportNames(Names(), [new ExportPlace(7, string.Empty), new ExportPlace(8, "First"), new ExportPlace(8, "Second")], []);

        Assert.False(names.TryGetWorldName(7, out _));
        Assert.Equal("World 7", ExportText.WorldName(names, 7));
        Assert.True(names.TryGetWorldName(8, out var first));
        Assert.Equal("First", first);
    }

    [Fact]
    public void ASummaryNamedThroughTheAdapterCarriesNamesAndNoIdentifiers()
    {
        var journal = MixedRun();

        var summary = RunSummaryBuilder.Build(RunId, journal.Entries, Adapter());
        var text = RunSummaryExporter.ToText(summary);
        var json = RunSummaryExporter.ToJson(summary);

        Assert.Contains("Iron Ore", text, StringComparison.Ordinal);
        Assert.Contains("Alpha", text, StringComparison.Ordinal);
        Assert.Contains("Beta", json, StringComparison.Ordinal);
        AssertNoIdentifiers(text);
        AssertNoIdentifiers(json);
    }

    private static ExportNames Adapter() => new(
        Names(),
        [new ExportPlace(Fixtures.Alpha.WorldId, "Alpha"), new ExportPlace(Fixtures.Beta.WorldId, "Beta"), new ExportPlace(Fixtures.Delta.WorldId, "Delta")],
        [new ExportPlace(Fixtures.PrimaryDataCenter, "Light"), new ExportPlace(Fixtures.SecondaryDataCenter, "Chaos")]);
}
