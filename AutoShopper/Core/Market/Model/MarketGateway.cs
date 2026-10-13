namespace AutoShopper.Core.Market.Model;

// Lifestream changes worlds at one of three city aetherytes, named by its WorldChangeAetheryte values; those values are
// the aetheryte rows of Ul'dah (9), Limsa Lominsa (8) and Gridania (2). Asking for the preferred market city's own
// aetheryte lands the character there after the change, a short aethernet ride from the board.
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
}
