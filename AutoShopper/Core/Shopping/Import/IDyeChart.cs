namespace AutoShopper.Core.Shopping.Import;

public readonly record struct DyeMatch(uint ItemId, string StainName);

public interface IDyeChart
{
    bool TryFind(uint color, out DyeMatch match);
}
