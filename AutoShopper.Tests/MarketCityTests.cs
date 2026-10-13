using System.Numerics;
using AutoShopper.Core.Market.Model;

namespace AutoShopper.Tests;

public sealed class MarketCityTests
{
    private const uint StepsOfNald = 130;
    private const uint NewGridania = 132;
    private const uint KuganeTerritory = 628;
    private const uint CoerthasCentralHighlands = 155;
    private const uint UldahAetheryte = 9;
    private const uint CrystariumAetheryte = 133;

    [Fact]
    public void CatalogIsIndexedByCity()
    {
        var cities = MarketBoardCatalog.Cities;

        Assert.Equal(Enum.GetValues<MarketCity>().Length, cities.Length);
        for (var index = 0; index < cities.Length; index++)
        {
            Assert.Equal((MarketCity)index, cities[index].City);
            Assert.False(cities[index].BoardPositions.IsDefaultOrEmpty);
            Assert.True(MarketBoardCatalog.IsBoardBaseId(cities[index].BoardBaseId));
        }
    }

    [Fact]
    public void OnlyTheHubCitiesEnterThroughTheAethernet()
    {
        Assert.True(MarketBoardCatalog.Of(MarketCity.Gridania).EntersThroughAethernet);
        Assert.True(MarketBoardCatalog.Of(MarketCity.Uldah).EntersThroughAethernet);
        Assert.True(MarketBoardCatalog.Of(MarketCity.Ishgard).EntersThroughAethernet);
        Assert.False(MarketBoardCatalog.Of(MarketCity.LimsaLominsa).EntersThroughAethernet);
        Assert.False(MarketBoardCatalog.Of(MarketCity.Kugane).EntersThroughAethernet);
        Assert.False(MarketBoardCatalog.Of(MarketCity.Tuliyollal).EntersThroughAethernet);
    }

    [Fact]
    public void NearestBoardIsTheClosestListedOne()
    {
        var uldah = MarketBoardCatalog.Of(MarketCity.Uldah);

        Assert.Equal(new Vector3(149.1f, 4.0f, -32.5f), MarketBoardCatalog.NearestBoard(uldah, new Vector3(140f, 4f, -30f)));
        Assert.Equal(new Vector3(125.8f, 4.1f, -95.1f), MarketBoardCatalog.NearestBoard(uldah, new Vector3(125f, 4f, -90f)));
    }

    [Fact]
    public void ChoiceStaysInTheCityTheCharacterStandsIn()
    {
        Assert.True(MarketCityChoice.TryChoose(KuganeTerritory, MarketCity.Uldah, NothingAttuned, out var city));
        Assert.Equal(MarketCity.Kugane, city);
    }

    [Fact]
    public void ChoiceStaysInACityWhoseHubTheCharacterStandsIn()
    {
        Assert.True(MarketCityChoice.TryChoose(StepsOfNald, MarketCity.LimsaLominsa, EverythingAttuned, out var city));
        Assert.Equal(MarketCity.Uldah, city);
        Assert.True(MarketCityChoice.TryChoose(NewGridania, MarketCity.Kugane, NothingAttuned, out city));
        Assert.Equal(MarketCity.Gridania, city);
    }

    [Fact]
    public void ChoiceGoesToThePreferredCityFromElsewhere()
    {
        Assert.True(MarketCityChoice.TryChoose(CoerthasCentralHighlands, MarketCity.Kugane, EverythingAttuned, out var city));
        Assert.Equal(MarketCity.Kugane, city);
        Assert.True(MarketCityChoice.TryChoose(CoerthasCentralHighlands, MarketCityChoice.DefaultPreferred, EverythingAttuned, out city));
        Assert.Equal(MarketCity.Uldah, city);
    }

    [Fact]
    public void ChoiceSkipsCitiesWhoseAetheryteIsNotAttuned()
    {
        Assert.True(MarketCityChoice.TryChoose(CoerthasCentralHighlands, MarketCity.Uldah, aetheryteId => aetheryteId != UldahAetheryte, out var city));
        Assert.Equal(MarketCity.LimsaLominsa, city);
        Assert.True(MarketCityChoice.TryChoose(CoerthasCentralHighlands, MarketCity.Uldah, aetheryteId => aetheryteId == CrystariumAetheryte, out city));
        Assert.Equal(MarketCity.Crystarium, city);
        Assert.False(MarketCityChoice.TryChoose(CoerthasCentralHighlands, MarketCity.Uldah, NothingAttuned, out _));
    }

    [Fact]
    public void ChoiceReadsAnUnknownPreferenceAsTheDefault()
    {
        Assert.True(MarketCityChoice.TryChoose(CoerthasCentralHighlands, (MarketCity)99, EverythingAttuned, out var city));
        Assert.Equal(MarketCityChoice.DefaultPreferred, city);
    }

    [Theory]
    [InlineData("uldah", nameof(MarketCity.Uldah))]
    [InlineData("Ul'dah", nameof(MarketCity.Uldah))]
    [InlineData("UL’DAH", nameof(MarketCity.Uldah))]
    [InlineData("thal", nameof(MarketCity.Uldah))]
    [InlineData("limsa", nameof(MarketCity.LimsaLominsa))]
    [InlineData("Gridania", nameof(MarketCity.Gridania))]
    [InlineData("old gridania", nameof(MarketCity.Gridania))]
    [InlineData("pillars", nameof(MarketCity.Ishgard))]
    [InlineData("kugane", nameof(MarketCity.Kugane))]
    [InlineData("the crystarium", nameof(MarketCity.Crystarium))]
    [InlineData("sharlayan", nameof(MarketCity.OldSharlayan))]
    [InlineData("Tuliyollal", nameof(MarketCity.Tuliyollal))]
    public void NamesResolveToTheirCity(string text, string expected)
    {
        Assert.True(MarketCityNames.TryParse(text, out var city));
        Assert.Equal(expected, city.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  '  ")]
    [InlineData("old")]
    [InlineData("the")]
    [InlineData("mor dhona")]
    public void AmbiguousOrUnknownNamesAreRefused(string text)
    {
        Assert.False(MarketCityNames.TryParse(text, out _));
    }

    [Theory]
    [InlineData(MarketCity.Uldah, 9)]
    [InlineData(MarketCity.LimsaLominsa, 8)]
    [InlineData(MarketCity.Gridania, 2)]
    [InlineData(MarketCity.Kugane, MarketGateway.None)]
    [InlineData(MarketCity.Tuliyollal, MarketGateway.None)]
    public void OnlyTheThreeStartingCitiesAreWorldChangeGateways(MarketCity city, int gateway)
    {
        Assert.Equal(gateway, MarketGateway.For(city));
    }

    private static bool EverythingAttuned(uint aetheryteId) => true;

    private static bool NothingAttuned(uint aetheryteId) => false;
}
