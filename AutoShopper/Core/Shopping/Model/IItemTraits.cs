namespace AutoShopper.Core.Shopping.Model;

public interface IItemTraits
{
    bool IsMarketable(uint itemId);

    bool CanBeHighQuality(uint itemId);
}
