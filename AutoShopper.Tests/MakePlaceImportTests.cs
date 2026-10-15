using AutoShopper.Core.Shopping.Import;
using static AutoShopper.Tests.ImportFixtures;

namespace AutoShopper.Tests;

public sealed class MakePlaceImportTests
{
    private const uint HinganInteriorWall = 20716;
    private const uint HinganFlooring = 20718;
    private const uint CottageRoof = 6347;
    private const uint SummoningBell = 7064;
    private const uint ManorBookshelf = 6569;
    private const uint WhitePartition = 24514;
    private const uint LeatherSofa = 28639;
    private const uint AssortedFruit = 7962;
    private const uint SpectrumDye = 52254;
    private const uint SootBlack = 0x2B2923;
    private const uint GobbiebagBrown = 0xB9A489;
    private const uint AldgoatBrown = 0xA2875C;

    [Fact]
    public void LayoutIsDetectedAsMakePlace()
    {
        Assert.Equal(ImportFormat.MakePlace, ListImporter.Detect(Read(MakePlaceLayout)));
        Assert.Equal(ImportFormat.MakePlace, ListImporter.Detect("{\"exteriorFurniture\":[]}"));
    }

    [Fact]
    public void OtherJsonIsNotMistakenForALayout()
    {
        Assert.Equal(ImportFormat.Json, ListImporter.Detect("{\"format\":\"AutoShopper.List\",\"version\":1,\"lines\":[]}"));
        Assert.Equal(ImportFormat.ArtisanList, ListImporter.Detect(Read(ArtisanList)));
    }

    [Fact]
    public void EveryPlacedPieceCountsOnce()
    {
        var result = ListImporter.Parse(Read(MakePlaceLayout));

        Assert.Equal(ImportFormat.MakePlace, result.Format);
        Assert.Equal(ImportError.None, result.Error);
        Assert.Equal(0, result.IgnoredFields);
        var quantities = ReadyQuantities(result);
        Assert.Equal(2, quantities[HinganInteriorWall]);
        Assert.Equal(1, quantities[HinganFlooring]);
        Assert.Equal(1, quantities[CottageRoof]);
        Assert.Equal(1, quantities[SummoningBell]);
        Assert.Equal(2, quantities[ManorBookshelf]);
        Assert.Equal(2, quantities[WhitePartition]);
        Assert.Equal(1, quantities[LeatherSofa]);
        Assert.Equal(1, quantities[AssortedFruit]);
        Assert.Equal(9, result.Rows.Count);
    }

    [Fact]
    public void DefaultFixturesAreNotBought()
    {
        var result = ListImporter.Parse(Read(MakePlaceLayout));

        for (var index = 0; index < result.Rows.Count; index++)
        {
            Assert.DoesNotContain("Goblet", result.Rows[index].SourceText);
            Assert.DoesNotContain("Stone Fence", result.Rows[index].SourceText);
        }
    }

    [Fact]
    public void WallpaperFromAnAssetPathIsLookedUpByName()
    {
        var result = ListImporter.Parse(Read(MakePlaceLayout));

        var wallpaper = Assert.Single(result.Rows, static row => row.RequestedItemId == 0);
        Assert.Equal("Masonwork Interior Wall", wallpaper.Name);
        Assert.Equal(1, wallpaper.Quantity);
    }

    [Theory]
    [InlineData("/Game/HouseAssets/Wallpaper/MasonworkInteriorWall/MasonworkInteriorWall.MasonworkInteriorWall", "Masonwork Interior Wall")]
    [InlineData("/Game/HouseAssets/Wallpaper/HinganInteriorWall", "Hingan Interior Wall")]
    [InlineData("SultanasBreathInteriorWall", "Sultanas Breath Interior Wall")]
    [InlineData("", "")]
    public void WallpaperNamesAreSpelledOut(string assetPath, string expected)
    {
        Assert.Equal(expected, MakePlaceParser.WallpaperName(assetPath));
    }

    [Fact]
    public void DyedPiecesAreCountedByColor()
    {
        var result = ListImporter.Parse(Read(MakePlaceLayout));

        Assert.Equal([new DyeImport(AldgoatBrown, 1), new DyeImport(GobbiebagBrown, 1), new DyeImport(SootBlack, 2)], result.Dyes);
    }

    [Fact]
    public void DyesBecomeOneRowPerDyeItem()
    {
        var layout = ListImporter.Parse(Read(MakePlaceLayout));
        var chart = new FakeDyeChart((SootBlack, SpectrumDye, "Soot Black"), (GobbiebagBrown, SpectrumDye, "Gobbiebag Brown"));

        var result = ListImporter.AddDyes(layout, chart);

        Assert.Equal(ImportError.None, result.Error);
        Assert.Equal(11, result.Rows.Count);
        var dye = RowFor(result, SpectrumDye)!;
        Assert.Equal(3, dye.Quantity);
        Assert.Equal("Gobbiebag Brown (1), Soot Black (2)", dye.SourceText);
        var unknown = result.Rows[^1];
        Assert.Equal("#A2875C", unknown.SourceText);
        Assert.Equal(ImportRowStatus.Invalid, unknown.Status);
        Assert.Equal(ImportRowProblem.UnknownDye, unknown.Problem);
        Assert.Equal(9, layout.Rows.Count);
    }

    [Fact]
    public void LayoutWithoutDyesIsReturnedAsIs()
    {
        var layout = ListImporter.Parse("{\"interiorFurniture\":[{\"itemId\":6569,\"name\":\"Manor Bookshelf\"}]}");

        Assert.Same(layout, ListImporter.AddDyes(layout, new FakeDyeChart()));
    }

    [Fact]
    public void UnsoldItemsAreLeftOut()
    {
        var result = ListImporter.Parse(Read(MakePlaceLayout));

        ListImporter.Resolve(result, new FakeItemResolver(ManorBookshelf, WhitePartition));

        Assert.Equal(ImportRowStatus.Resolved, RowFor(result, ManorBookshelf)!.Status);
        var bell = RowFor(result, SummoningBell)!;
        Assert.Equal(ImportRowStatus.Invalid, bell.Status);
        Assert.Equal(ImportRowProblem.NotMarketable, bell.Problem);
        Assert.Equal(ImportRowStatus.Unknown, Assert.Single(result.Rows, static row => row.RequestedItemId == 0).Status);
    }

    [Fact]
    public void UnknownTopLevelFieldsAreCounted()
    {
        var result = ListImporter.Parse("{\"houseSize\":\"Large\",\"futureField\":1,\"interiorFurniture\":[{\"itemId\":6569}]}");

        Assert.Equal(1, result.IgnoredFields);
        Assert.Equal("6569", Assert.Single(result.Rows).SourceText);
    }

    [Fact]
    public void LayoutWithNothingPlacedIsEmpty()
    {
        var result = ListImporter.Parse("{\"interiorFurniture\":[],\"exteriorFixture\":[{\"type\":\"Fence\",\"itemId\":0}]}");

        Assert.Equal(ImportFormat.MakePlace, result.Format);
        Assert.Equal(ImportError.Empty, result.Error);
    }

    [Fact]
    public void JsonWithoutPlacementsIsTheWrongFormat()
    {
        var result = ListImporter.Parse("{\"houseSize\":\"Small\"}", ImportFormat.MakePlace);

        Assert.Equal(ImportError.WrongFormat, result.Error);
    }

    [Fact]
    public void BrokenLayoutIsReported()
    {
        var text = Read(MakePlaceLayout);

        var result = ListImporter.Parse(text[..^40], ImportFormat.MakePlace);

        Assert.Equal(ImportError.MalformedJson, result.Error);
    }

    private sealed class FakeDyeChart(params (uint Color, uint ItemId, string Name)[] dyes) : IDyeChart
    {
        public bool TryFind(uint color, out DyeMatch match)
        {
            for (var index = 0; index < dyes.Length; index++)
            {
                if (dyes[index].Color == color)
                {
                    match = new DyeMatch(dyes[index].ItemId, dyes[index].Name);
                    return true;
                }
            }

            match = default;
            return false;
        }
    }
}
