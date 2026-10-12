namespace AutoShopper.Core.Shopping.Model;

public sealed class ListAnalysis
{
    private readonly uint[] itemIds;

    private ListAnalysis(NormalizedList normalized, ListValidation validation, uint[] itemIds, long totalUnits)
    {
        Normalized = normalized;
        Validation = validation;
        this.itemIds = itemIds;
        TotalUnits = totalUnits;
    }

    public NormalizedList Normalized { get; }

    public ListValidation Validation { get; }

    public long TotalUnits { get; }

    public int LineCount => Validation.LineCount;

    // Each item once, in the order the list first names it.
    public ReadOnlySpan<uint> ItemIds => itemIds;

    public static ListAnalysis Build(ShoppingList list, IItemTraits? traits)
    {
        var lines = list.Lines;
        var distinct = new HashSet<uint>(lines.Count);
        var itemIds = new List<uint>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var itemId = lines[index].ItemId;
            if (itemId != 0 && distinct.Add(itemId))
            {
                itemIds.Add(itemId);
            }
        }

        return new ListAnalysis(ListNormalizer.Normalize(lines), ListNormalizer.Validate(lines, traits), itemIds.ToArray(), list.TotalUnits());
    }
}
