namespace AutoShopper.Core.Shopping.Model;

public readonly record struct OwnedCount(int NormalQuality, int HighQuality)
{
    public int Total => NormalQuality + HighQuality;

    public int Of(ItemQuality quality) => quality switch
    {
        ItemQuality.HighQuality => HighQuality,
        ItemQuality.NormalQuality => NormalQuality,
        _ => Total,
    };
}
