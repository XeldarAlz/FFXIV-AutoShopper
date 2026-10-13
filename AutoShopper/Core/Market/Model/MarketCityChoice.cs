namespace AutoShopper.Core.Market.Model;

internal static class MarketCityChoice
{
    public const MarketCity DefaultPreferred = MarketCity.Uldah;

    // A city the character already stands in is kept, and so is one whose aetheryte hub it stands in, since the hub's
    // aethernet reaches the board without a teleport. Otherwise the preferred city, then the catalog's order, each only
    // when its aetheryte is attuned, because the game refuses a teleport to one that is not.
    public static bool TryChoose(uint currentTerritoryId, MarketCity preferred, Func<uint, bool> isAttuned, out MarketCity city)
    {
        var cities = MarketBoardCatalog.Cities;
        for (var index = 0; index < cities.Length; index++)
        {
            if (cities[index].TerritoryId == currentTerritoryId || cities[index].TeleportTerritoryId == currentTerritoryId)
            {
                city = cities[index].City;
                return true;
            }
        }

        var wanted = MarketBoardCatalog.Contains(preferred) ? preferred : DefaultPreferred;
        if (isAttuned(MarketBoardCatalog.Of(wanted).TeleportAetheryteId))
        {
            city = wanted;
            return true;
        }

        for (var index = 0; index < cities.Length; index++)
        {
            if (isAttuned(cities[index].TeleportAetheryteId))
            {
                city = cities[index].City;
                return true;
            }
        }

        city = wanted;
        return false;
    }
}
