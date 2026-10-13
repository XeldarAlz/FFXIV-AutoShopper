using AutoShopper.Core.Exports;
using AutoShopper.Core.Travel;

namespace AutoShopper.Core.Items;

// Exports name items from the catalog and worlds from the game's World sheet; built once, on the first export.
internal static class GameExportNames
{
    private static ExportNames? cached;

    public static IExportNames For(ItemCatalog catalog) => cached ??= Build(catalog);

    private static ExportNames Build(ItemCatalog catalog)
    {
        var worlds = Worlds.Public;
        var worldPlaces = new ExportPlace[worlds.Length];
        var dataCenterPlaces = new List<ExportPlace>();
        for (var index = 0; index < worlds.Length; index++)
        {
            var world = worlds[index];
            worldPlaces[index] = new ExportPlace(world.Id, world.Name);
            if (!Contains(dataCenterPlaces, world.DataCenterId))
            {
                dataCenterPlaces.Add(new ExportPlace(world.DataCenterId, world.DataCenterName));
            }
        }

        return new ExportNames(catalog, worldPlaces, dataCenterPlaces.ToArray());
    }

    private static bool Contains(List<ExportPlace> places, uint id)
    {
        for (var index = 0; index < places.Count; index++)
        {
            if (places[index].Id == id)
            {
                return true;
            }
        }

        return false;
    }
}
