using AutoShopper.Core.Localization;
using AutoShopper.Core.Shopping.Import;
using AutoShopper.Core.Shopping.Model;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Globalization;
using System.Threading.Tasks;

namespace AutoShopper.Core.Items;

// Every item the market board sells, named in the client's language. The game data is read once, off the draw thread,
// the first time anything needs it; the snapshot never changes afterwards, so any thread may read it.
internal sealed partial class ItemCatalog : IItemNameResolver, IItemTraits, IItemNames
{
    private const uint CrystalUiCategoryId = 59;
    // Lumina's item sheet holds a little under fifty thousand rows, about seventeen thousand of them marketable.
    private const int ExpectedMarketableItems = 20_000;

    private readonly object gate = new();
    private Task? loading;
    private volatile Snapshot? snapshot;

    public bool IsReady => snapshot is not null;

    public bool LoadFailed { get; private set; }

    public int Count => snapshot?.Ids.Length ?? 0;

    // Non-ASCII letters used by item names, in ImGui's start/end pair form, for the font atlas.
    public ushort[] NameGlyphRanges => snapshot?.GlyphRanges ?? [0];

    public Task WhenReady()
    {
        lock (gate)
        {
            loading ??= Task.Run(Load);
            return loading;
        }
    }

    public void EnsureLoading()
    {
        if (snapshot is null && loading is null)
        {
            WhenReady();
        }
    }

    public bool TryGet(uint itemId, out CatalogItem item)
    {
        var current = snapshot;
        if (current is null || !current.IndexById.TryGetValue(itemId, out var index))
        {
            item = default;
            return false;
        }

        item = current.At(index);
        return true;
    }

    public CatalogItem At(int index) => snapshot!.At(index);

    public bool IsMarketable(uint itemId) => snapshot?.IndexById.ContainsKey(itemId) ?? false;

    public bool CanBeHighQuality(uint itemId)
        => TryGet(itemId, out var item) && item.CanBeHighQuality;

    public bool TryGetName(uint itemId, out string name)
    {
        if (TryGet(itemId, out var item))
        {
            name = item.Name;
            return true;
        }

        name = string.Empty;
        return false;
    }

    public NameResolution Resolve(string nameOrId)
    {
        var current = snapshot;
        if (current is null)
        {
            return NameResolution.None;
        }

        var trimmed = nameOrId.AsSpan().Trim();
        if (IsDigits(trimmed))
        {
            return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId) && current.IndexById.ContainsKey(itemId)
                ? NameResolution.Unique(itemId)
                : NameResolution.None;
        }

        if (!current.IndexByName.TryGetValue(Fold(nameOrId), out var index))
        {
            return NameResolution.None;
        }

        return index >= 0
            ? NameResolution.Unique(current.Ids[index])
            : NameResolution.Ambiguous((uint[])current.AmbiguousGroups[~index].Clone());
    }

    private void Load()
    {
        try
        {
            var built = Build();
            snapshot = built;
            RunLog.Info($"Item catalog ready: {built.Ids.Length} marketable items.");
        }
        catch (Exception exception)
        {
            LoadFailed = true;
            RunLog.Error(exception, "The item catalog failed to load");
        }
    }

    private static Snapshot Build()
    {
        var sheet = Svc.Data.GetExcelSheet<Item>();
        var entries = new List<CatalogItem>(ExpectedMarketableItems);
        for (var rowIndex = 0; rowIndex < sheet.Count; rowIndex++)
        {
            var row = sheet.GetRowAt(rowIndex);
            if (row.ItemSearchCategory.RowId == 0 || row.IsUntradable)
            {
                continue;
            }

            var name = row.Name.ExtractText().Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var flags = CatalogItemFlags.None;
            if (row.CanBeHq)
            {
                flags |= CatalogItemFlags.CanBeHighQuality;
            }

            if (row.IsUnique)
            {
                flags |= CatalogItemFlags.Unique;
            }

            if (row.ItemUICategory.RowId == CrystalUiCategoryId)
            {
                flags |= CatalogItemFlags.Crystal;
            }

            entries.Add(new CatalogItem(row.RowId, name, row.Icon, (ushort)Math.Min(row.StackSize, ushort.MaxValue), flags));
        }

        entries.Sort(static (left, right) =>
        {
            var byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : left.Id.CompareTo(right.Id);
        });

        return new Snapshot(entries);
    }

    private static bool IsDigits(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsAsciiDigit(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class Snapshot
    {
        public readonly uint[] Ids;
        public readonly string[] Names;
        public readonly string[] FoldedNames;
        public readonly ushort[] IconIds;
        public readonly ushort[] StackSizes;
        public readonly CatalogItemFlags[] Flags;
        public readonly Dictionary<uint, int> IndexById;

        // A value at or above zero is the index of the only item with that name; a negative value is the bitwise
        // complement of an index into AmbiguousGroups, for names several items share.
        public readonly Dictionary<string, int> IndexByName;
        public readonly List<uint[]> AmbiguousGroups = [];
        public readonly ushort[] GlyphRanges;

        public Snapshot(List<CatalogItem> entries)
        {
            var count = entries.Count;
            Ids = new uint[count];
            Names = new string[count];
            FoldedNames = new string[count];
            IconIds = new ushort[count];
            StackSizes = new ushort[count];
            Flags = new CatalogItemFlags[count];
            IndexById = new Dictionary<uint, int>(count);
            IndexByName = new Dictionary<string, int>(count, StringComparer.Ordinal);
            var glyphs = new bool[Localization.GlyphRanges.CodepointCount];

            for (var index = 0; index < count; index++)
            {
                var entry = entries[index];
                Ids[index] = entry.Id;
                Names[index] = entry.Name;
                FoldedNames[index] = Fold(entry.Name);
                IconIds[index] = (ushort)entry.IconId;
                StackSizes[index] = entry.StackSize;
                Flags[index] = entry.Flags;
                IndexById[entry.Id] = index;
                AddName(FoldedNames[index], index);
                Localization.GlyphRanges.MarkText(glyphs, entry.Name);
            }

            GlyphRanges = Localization.GlyphRanges.ToRanges(glyphs);
        }

        public CatalogItem At(int index) => new(Ids[index], Names[index], IconIds[index], StackSizes[index], Flags[index]);

        private void AddName(string folded, int index)
        {
            if (!IndexByName.TryGetValue(folded, out var existing))
            {
                IndexByName[folded] = index;
                return;
            }

            if (existing >= 0)
            {
                AmbiguousGroups.Add([Ids[existing], Ids[index]]);
                IndexByName[folded] = ~(AmbiguousGroups.Count - 1);
                return;
            }

            var group = AmbiguousGroups[~existing];
            var grown = new uint[group.Length + 1];
            group.CopyTo(grown, 0);
            grown[^1] = Ids[index];
            AmbiguousGroups[~existing] = grown;
        }
    }
}
