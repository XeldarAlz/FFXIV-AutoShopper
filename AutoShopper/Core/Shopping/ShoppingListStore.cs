using AutoShopper.Core.Inventory;
using AutoShopper.Core.Items;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Shopping.Import;
using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Core.Shopping;

internal enum AddLineResult : byte
{
    Added,
    Merged,
    ListFull,
}

// Every change to the shopping lists goes through here, so the configuration is saved and the revision that keys the
// window's cached text and the list analysis moves with each edit.
internal sealed class ShoppingListStore
{
    private readonly Configuration configuration;
    private readonly ItemCatalog catalog;
    private readonly InventoryReader inventory;
    private readonly Predicate<uint> isCrystal;

    private ListAnalysis? analysis;
    private ShoppingList? analysedList;
    private int analysedRevision = -1;
    private bool analysedWithItems;

    public ShoppingListStore(Configuration configuration, ItemCatalog catalog, InventoryReader inventory)
    {
        this.configuration = configuration;
        this.catalog = catalog;
        this.inventory = inventory;
        isCrystal = IsCrystal;
        if (Repair())
        {
            configuration.Save();
        }
    }

    public int Revision { get; private set; }

    public IReadOnlyList<ShoppingList> Lists => configuration.Lists;

    public ShoppingList? Selected => Find(configuration.SelectedListId);

    public ShoppingList? Find(Guid listId)
    {
        var lists = configuration.Lists;
        for (var index = 0; index < lists.Count; index++)
        {
            if (lists[index].Id == listId)
            {
                return lists[index];
            }
        }

        return null;
    }

    public int CountArchived()
    {
        var count = 0;
        var lists = configuration.Lists;
        for (var index = 0; index < lists.Count; index++)
        {
            if (lists[index].Archived)
            {
                count++;
            }
        }

        return count;
    }

    public void Select(ShoppingList list)
    {
        if (configuration.SelectedListId == list.Id)
        {
            return;
        }

        configuration.SelectedListId = list.Id;
        Commit();
    }

    public ShoppingList CreateList(string? name = null)
    {
        var list = ShoppingList.Create(string.IsNullOrWhiteSpace(name) ? NextDefaultName() : name, DateTime.UtcNow);
        configuration.Lists.Add(list);
        configuration.SelectedListId = list.Id;
        Commit();
        return list;
    }

    public void Rename(ShoppingList list, string name)
    {
        var cleaned = ShoppingLimits.CleanName(name, ShoppingLimits.MaximumListNameLength);
        if (cleaned.Length == 0 || string.Equals(cleaned, list.Name, StringComparison.Ordinal))
        {
            return;
        }

        list.Name = cleaned;
        Touch(list);
    }

    public ShoppingList Duplicate(ShoppingList list)
    {
        var copy = list.Duplicate(Loc.T(L.Lists.CopyName, list.Name), DateTime.UtcNow);
        var index = configuration.Lists.IndexOf(list);
        configuration.Lists.Insert(index < 0 ? configuration.Lists.Count : index + 1, copy);
        configuration.SelectedListId = copy.Id;
        Commit();
        return copy;
    }

    public void SetArchived(ShoppingList list, bool archived)
    {
        if (list.Archived == archived)
        {
            return;
        }

        list.Archived = archived;
        Touch(list);
    }

    public void Delete(ShoppingList list)
    {
        if (!configuration.Lists.Remove(list))
        {
            return;
        }

        if (configuration.SelectedListId == list.Id)
        {
            configuration.SelectedListId = FirstActiveId();
        }

        Revision++;
        configuration.Save();
    }

    // An existing Buy line for the same item at Any quality takes the added units, so adding an item twice raises one
    // line instead of leaving two lines that only merge at planning time.
    public AddLineResult AddItem(ShoppingList list, uint itemId, int quantity, out ShoppingLine? line)
    {
        var lines = list.Lines;
        for (var index = 0; index < lines.Count; index++)
        {
            var existing = lines[index];
            if (existing.ItemId != itemId || existing.Quality != ItemQuality.Any || existing.Mode != QuantityMode.Buy)
            {
                continue;
            }

            existing.Quantity = ShoppingLimits.ClampQuantity((long)existing.Quantity + quantity);
            line = existing;
            Touch(list);
            return AddLineResult.Merged;
        }

        if (lines.Count >= ShoppingLimits.MaximumLinesPerList)
        {
            line = null;
            return AddLineResult.ListFull;
        }

        line = ShoppingLine.Create(itemId, ShoppingLimits.ClampQuantity(quantity), ItemQuality.Any, QuantityMode.Buy);
        lines.Add(line);
        Touch(list);
        return AddLineResult.Added;
    }

    public CrystalRestockOutcome AddCrystals(ShoppingList list, CrystalElements elements, CrystalKinds kinds, int target)
    {
        var outcome = CrystalRestock.Apply(list.Lines, elements, kinds, target, isCrystal);
        if (outcome.Added + outcome.Updated > 0)
        {
            Touch(list);
        }

        return outcome;
    }

    public void RemoveLine(ShoppingList list, Guid lineId)
    {
        var index = list.IndexOfLine(lineId);
        if (index < 0)
        {
            return;
        }

        list.Lines.RemoveAt(index);
        Touch(list);
    }

    public void LineEdited(ShoppingList list) => Touch(list);

    public int ApplyImport(ShoppingList list, IReadOnlyList<ImportRow> rows, bool replace)
    {
        if (replace)
        {
            list.Lines.Clear();
        }

        var added = AppendRows(list, rows);
        Touch(list);
        return added;
    }

    public ShoppingList CreateFromImport(string? name, IReadOnlyList<ImportRow> rows, out int added)
    {
        var list = CreateList(name);
        added = AppendRows(list, rows);
        Touch(list);
        return list;
    }

    public ListAnalysis Analyze(ShoppingList list)
    {
        var withItems = catalog.IsReady;
        if (analysis is not null && ReferenceEquals(analysedList, list) && analysedRevision == Revision && analysedWithItems == withItems)
        {
            return analysis;
        }

        analysis = ListAnalysis.Build(list, withItems ? catalog : null);
        analysedList = list;
        analysedRevision = Revision;
        analysedWithItems = withItems;
        inventory.Watch(analysis.ItemIds);
        return analysis;
    }

    private bool IsCrystal(uint itemId) => catalog.TryGet(itemId, out var item) && item.IsCrystal;

    private static int AppendRows(ShoppingList list, IReadOnlyList<ImportRow> rows)
    {
        var lines = list.Lines;
        var usedIds = new HashSet<Guid>(lines.Count + rows.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            usedIds.Add(lines[index].Id);
        }

        var added = 0;
        for (var index = 0; index < rows.Count && lines.Count < ShoppingLimits.MaximumLinesPerList; index++)
        {
            var row = rows[index];
            if (!row.IsReady)
            {
                continue;
            }

            var lineId = row.LineId != Guid.Empty && usedIds.Add(row.LineId) ? row.LineId : NewLineId(usedIds);
            lines.Add(row.ToLine(lineId));
            added++;
        }

        return added;
    }

    private static Guid NewLineId(HashSet<Guid> usedIds)
    {
        var lineId = Guid.NewGuid();
        usedIds.Add(lineId);
        return lineId;
    }

    private void Touch(ShoppingList list)
    {
        list.ModifiedAtUtc = DateTime.UtcNow;
        Commit();
    }

    private void Commit()
    {
        Revision++;
        configuration.SaveDebounced();
    }

    private string NextDefaultName()
    {
        var name = Loc.T(L.Lists.DefaultName);
        for (var number = 2; NameTaken(name); number++)
        {
            name = Loc.T(L.Lists.DefaultNameNumbered, number);
        }

        return name;
    }

    private bool NameTaken(string name)
    {
        var lists = configuration.Lists;
        for (var index = 0; index < lists.Count; index++)
        {
            if (string.Equals(lists[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private Guid FirstActiveId()
    {
        var lists = configuration.Lists;
        for (var index = 0; index < lists.Count; index++)
        {
            if (!lists[index].Archived)
            {
                return lists[index].Id;
            }
        }

        return lists.Count > 0 ? lists[0].Id : Guid.Empty;
    }

    private bool Repair()
    {
        var changed = false;
        if (configuration.Lists is null)
        {
            configuration.Lists = [];
            changed = true;
        }

        var lists = configuration.Lists;
        var seen = new HashSet<Guid>(lists.Count);
        for (var index = lists.Count - 1; index >= 0; index--)
        {
            if (lists[index] is null)
            {
                lists.RemoveAt(index);
                changed = true;
            }
        }

        for (var index = 0; index < lists.Count; index++)
        {
            var list = lists[index];
            changed |= list.Repair();
            if (seen.Add(list.Id))
            {
                continue;
            }

            list.Id = Guid.NewGuid();
            seen.Add(list.Id);
            changed = true;
        }

        if (Selected is null && configuration.SelectedListId != FirstActiveId())
        {
            configuration.SelectedListId = FirstActiveId();
            changed = true;
        }

        return changed;
    }
}
