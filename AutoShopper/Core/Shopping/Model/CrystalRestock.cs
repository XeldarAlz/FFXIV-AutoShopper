using System.Numerics;

namespace AutoShopper.Core.Shopping.Model;

[Flags]
public enum CrystalElements : byte
{
    None = 0,
    Fire = 1 << 0,
    Ice = 1 << 1,
    Wind = 1 << 2,
    Earth = 1 << 3,
    Lightning = 1 << 4,
    Water = 1 << 5,
    All = Fire | Ice | Wind | Earth | Lightning | Water,
}

[Flags]
public enum CrystalKinds : byte
{
    None = 0,
    Shard = 1 << 0,
    Crystal = 1 << 1,
    Cluster = 1 << 2,
    All = Shard | Crystal | Cluster,
}

public readonly record struct CrystalRestockOutcome(int Added, int Updated, int LeftOut);

// Each picked crystal becomes one Top up line at any quality, so a run buys only what the bag is missing. A Top up line
// already on the list for that crystal takes the new target instead of a second line counting against it.
public static class CrystalRestock
{
    public const int ElementCount = 6;
    public const int KindCount = 3;

    // The crystal bag holds at most 9,999 of each crystal, so a higher target could never be reached.
    public const int MaximumTarget = 9_999;

    // Checked against Lumina's Item sheet: rows 2 to 19 are the eighteen marketable items of ItemUICategory 59, Fire,
    // Ice, Wind, Earth, Lightning and Water shards (2 to 7), then crystals (8 to 13), then clusters (14 to 19).
    private const uint FirstCrystalItemId = 2;

    public static uint ItemIdOf(int elementIndex, int kindIndex)
        => FirstCrystalItemId + (uint)(kindIndex * ElementCount + elementIndex);

    public static bool Has(CrystalElements elements, int elementIndex) => ((int)elements & (1 << elementIndex)) != 0;

    public static bool Has(CrystalKinds kinds, int kindIndex) => ((int)kinds & (1 << kindIndex)) != 0;

    public static int Count(CrystalElements elements, CrystalKinds kinds)
        => BitOperations.PopCount((uint)(elements & CrystalElements.All)) * BitOperations.PopCount((uint)(kinds & CrystalKinds.All));

    public static int ClampTarget(long target) => (int)Math.Clamp(target, ShoppingLimits.MinimumQuantity, MaximumTarget);

    // isCrystal confirms each id against the game's own data before it lands on the list; an id it refuses is left out.
    public static CrystalRestockOutcome Apply(List<ShoppingLine> lines, CrystalElements elements, CrystalKinds kinds, int target, Predicate<uint> isCrystal)
    {
        var quantity = ClampTarget(target);
        var added = 0;
        var updated = 0;
        var leftOut = 0;
        for (var kindIndex = 0; kindIndex < KindCount; kindIndex++)
        {
            if (!Has(kinds, kindIndex))
            {
                continue;
            }

            for (var elementIndex = 0; elementIndex < ElementCount; elementIndex++)
            {
                if (!Has(elements, elementIndex))
                {
                    continue;
                }

                var itemId = ItemIdOf(elementIndex, kindIndex);
                if (!isCrystal(itemId))
                {
                    leftOut++;
                    continue;
                }

                var existing = IndexOfTopUp(lines, itemId);
                if (existing >= 0)
                {
                    lines[existing].Quantity = quantity;
                    updated++;
                    continue;
                }

                if (lines.Count >= ShoppingLimits.MaximumLinesPerList)
                {
                    leftOut++;
                    continue;
                }

                lines.Add(ShoppingLine.Create(itemId, quantity, ItemQuality.Any, QuantityMode.ReachInventory));
                added++;
            }
        }

        return new CrystalRestockOutcome(added, updated, leftOut);
    }

    private static int IndexOfTopUp(List<ShoppingLine> lines, uint itemId)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.ItemId == itemId && line.Quality == ItemQuality.Any && line.Mode == QuantityMode.ReachInventory)
            {
                return index;
            }
        }

        return -1;
    }
}
