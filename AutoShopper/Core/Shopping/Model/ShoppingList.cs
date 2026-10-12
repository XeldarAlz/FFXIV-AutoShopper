namespace AutoShopper.Core.Shopping.Model;

[Serializable]
public sealed class ShoppingList
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<ShoppingLine> Lines { get; set; } = [];

    public bool Archived { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ModifiedAtUtc { get; set; }

    public static ShoppingList Create(string name, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        Name = ShoppingLimits.CleanName(name, ShoppingLimits.MaximumListNameLength),
        CreatedAtUtc = nowUtc,
        ModifiedAtUtc = nowUtc,
    };

    public ShoppingList Duplicate(string name, DateTime nowUtc)
    {
        var copy = Create(name, nowUtc);
        copy.Lines.Capacity = Lines.Count;
        for (var index = 0; index < Lines.Count; index++)
        {
            copy.Lines.Add(Lines[index].CopyWithNewId());
        }

        return copy;
    }

    public int IndexOfLine(Guid lineId)
    {
        for (var index = 0; index < Lines.Count; index++)
        {
            if (Lines[index].Id == lineId)
            {
                return index;
            }
        }

        return -1;
    }

    public long TotalUnits()
    {
        var total = 0L;
        for (var index = 0; index < Lines.Count; index++)
        {
            total = checked(total + Math.Max(0, Lines[index].Quantity));
        }

        return total;
    }

    // A hand-edited or partly written configuration can carry a missing line list, empty ids or ids copied between
    // lines; every line needs its own id because edits and imports address lines by it. True when anything changed.
    public bool Repair()
    {
        var changed = false;
        if (Lines is null)
        {
            Lines = [];
            changed = true;
        }

        if (Name is null)
        {
            Name = string.Empty;
            changed = true;
        }

        if (Id == Guid.Empty)
        {
            Id = Guid.NewGuid();
            changed = true;
        }

        for (var index = Lines.Count - 1; index >= 0; index--)
        {
            if (Lines[index] is null)
            {
                Lines.RemoveAt(index);
                changed = true;
            }
        }

        var seen = new HashSet<Guid>(Lines.Count);
        for (var index = 0; index < Lines.Count; index++)
        {
            var line = Lines[index];
            if (line.Id != Guid.Empty && seen.Add(line.Id))
            {
                continue;
            }

            line.Id = Guid.NewGuid();
            seen.Add(line.Id);
            changed = true;
        }

        return changed;
    }
}
