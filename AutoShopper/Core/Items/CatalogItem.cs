namespace AutoShopper.Core.Items;

[Flags]
internal enum CatalogItemFlags : byte
{
    None = 0,
    CanBeHighQuality = 1 << 0,
    Unique = 1 << 1,
    Crystal = 1 << 2,
}

internal readonly record struct CatalogItem(uint Id, string Name, uint IconId, ushort StackSize, CatalogItemFlags Flags)
{
    public bool CanBeHighQuality => (Flags & CatalogItemFlags.CanBeHighQuality) != 0;

    public bool IsUnique => (Flags & CatalogItemFlags.Unique) != 0;

    public bool IsCrystal => (Flags & CatalogItemFlags.Crystal) != 0;
}
