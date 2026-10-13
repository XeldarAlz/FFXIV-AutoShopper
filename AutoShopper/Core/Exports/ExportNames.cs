using AutoShopper.Core.Shopping.Import;

namespace AutoShopper.Core.Exports;

internal readonly record struct ExportPlace(uint Id, string Name);

// Item names come from the catalog as each export is written; worlds and data centers are copied once, since the game
// data they come from never changes while the plugin runs.
internal sealed class ExportNames : IExportNames
{
    private readonly IItemNames items;
    private readonly Dictionary<uint, string> worlds;
    private readonly Dictionary<uint, string> dataCenters;

    public ExportNames(IItemNames items, ReadOnlySpan<ExportPlace> worlds, ReadOnlySpan<ExportPlace> dataCenters)
    {
        this.items = items;
        this.worlds = ToTable(worlds);
        this.dataCenters = ToTable(dataCenters);
    }

    public bool TryGetName(uint itemId, out string name) => items.TryGetName(itemId, out name);

    public bool TryGetWorldName(uint worldId, out string name) => Find(worlds, worldId, out name);

    public bool TryGetDataCenterName(uint dataCenterId, out string name) => Find(dataCenters, dataCenterId, out name);

    private static Dictionary<uint, string> ToTable(ReadOnlySpan<ExportPlace> places)
    {
        var table = new Dictionary<uint, string>(places.Length);
        for (var index = 0; index < places.Length; index++)
        {
            var place = places[index];
            if (place.Name.Length > 0)
            {
                table.TryAdd(place.Id, place.Name);
            }
        }

        return table;
    }

    private static bool Find(Dictionary<uint, string> table, uint id, out string name)
    {
        if (table.TryGetValue(id, out var found))
        {
            name = found;
            return true;
        }

        name = string.Empty;
        return false;
    }
}
