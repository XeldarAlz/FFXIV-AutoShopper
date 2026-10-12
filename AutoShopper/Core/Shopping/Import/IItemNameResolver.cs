namespace AutoShopper.Core.Shopping.Import;

public interface IItemNameResolver
{
    // An exact item name, ignoring case, or a decimal item id.
    NameResolution Resolve(string nameOrId);
}
