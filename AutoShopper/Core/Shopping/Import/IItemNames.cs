namespace AutoShopper.Core.Shopping.Import;

public interface IItemNames
{
    bool TryGetName(uint itemId, out string name);
}
