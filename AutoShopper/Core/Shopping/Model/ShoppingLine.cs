namespace AutoShopper.Core.Shopping.Model;

[Serializable]
public sealed class ShoppingLine
{
    public Guid Id { get; set; }

    public uint ItemId { get; set; }

    public ItemQuality Quality { get; set; }

    public QuantityMode Mode { get; set; }

    public int Quantity { get; set; } = ShoppingLimits.MinimumQuantity;

    // Zero sets no cap of its own; the run's spending limits still apply.
    public long MaxUnitPrice { get; set; }

    public int ExcessAllowance { get; set; }

    public static ShoppingLine Create(uint itemId, int quantity, ItemQuality quality, QuantityMode mode) => new()
    {
        Id = Guid.NewGuid(),
        ItemId = itemId,
        Quantity = quantity,
        Quality = quality,
        Mode = mode,
    };

    public ShoppingLine CopyWithNewId() => new()
    {
        Id = Guid.NewGuid(),
        ItemId = ItemId,
        Quality = Quality,
        Mode = Mode,
        Quantity = Quantity,
        MaxUnitPrice = MaxUnitPrice,
        ExcessAllowance = ExcessAllowance,
    };

    public DemandKey Key() => new(ItemId, Quality, Mode);
}
