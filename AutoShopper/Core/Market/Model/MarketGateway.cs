namespace AutoShopper.Core.Market.Model;

// Lifestream changes worlds at one of three city aetherytes, named by its WorldChangeAetheryte values; those values are
// the aetheryte rows of Ul'dah (9), Limsa Lominsa (8) and Gridania (2). Asking for the preferred market city's own
// aetheryte lands the character there after the change, a short aethernet ride from the board.
// None leaves the city to Lifestream's own setting, which is Ul'dah unless the player changed it.
internal static class MarketGateway
{
    public const int None = 0;

    public static int For(MarketCity city) => city switch
    {
        MarketCity.Uldah => 9,
        MarketCity.LimsaLominsa => 8,
        MarketCity.Gridania => 2,
        _ => None,
    };

    // Lifestream teleports to the gateway to make the change, so one whose aetheryte is not attuned is no gateway.
    public static int Through(MarketCity city, Func<uint, bool> isAttuned)
    {
        var gateway = For(city);
        return gateway != None && isAttuned(MarketBoardCatalog.Of(city).TeleportAetheryteId) ? gateway : None;
    }

    // Changes worlds in the gateway city the run ended in, so the character stays where it shopped.
    public static int ForWayHome(uint currentTerritoryId, MarketCity preferred, Func<uint, bool> isAttuned)
    {
        var here = MarketBoardCatalog.TryFindCityOrHub(currentTerritoryId, out var city) ? Through(city.City, isAttuned) : None;
        return here != None ? here : Through(preferred, isAttuned);
    }
}
