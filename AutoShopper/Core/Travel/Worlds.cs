using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoShopper.Core.Travel;

internal enum WorldRegion : byte
{
    Unknown = 0,
    Japan = 1,
    NorthAmerica = 2,
    Europe = 3,
    Oceania = 4,
}

internal readonly record struct WorldInfo(uint Id, string Name, uint DataCenterId, string DataCenterName, WorldRegion Region);

internal readonly record struct DataCenterInfo(uint Id, string Name);

// Every public world a character can visit, read once from the World and WorldDCGroupType sheets.
internal static class Worlds
{
    private const string CloudDataCenterMarker = "Cloud";

    private static readonly Dictionary<WorldRegion, DataCenterInfo[]> dataCentersByRegion = new();
    private static readonly Dictionary<WorldRegion, WorldInfo[]> worldsByRegion = new();

    private static WorldInfo[]? all;

    private static WorldInfo[] All => all ??= Build();

    public static ReadOnlySpan<WorldInfo> Public => All;

    // The data centers of a region in sheet order, each once.
    public static DataCenterInfo[] DataCentersIn(WorldRegion region)
    {
        if (dataCentersByRegion.TryGetValue(region, out var cached))
        {
            return cached;
        }

        var worlds = All;
        var found = new List<DataCenterInfo>();
        for (var index = 0; index < worlds.Length; index++)
        {
            var world = worlds[index];
            if (world.Region != region || ContainsDataCenter(found, world.DataCenterId))
            {
                continue;
            }

            found.Add(new DataCenterInfo(world.DataCenterId, world.DataCenterName));
        }

        var resolved = found.ToArray();
        dataCentersByRegion[region] = resolved;
        return resolved;
    }

    // The worlds of a region in sheet order.
    public static WorldInfo[] InRegion(WorldRegion region)
    {
        if (worldsByRegion.TryGetValue(region, out var cached))
        {
            return cached;
        }

        var worlds = All;
        var found = new List<WorldInfo>();
        for (var index = 0; index < worlds.Length; index++)
        {
            if (worlds[index].Region == region)
            {
                found.Add(worlds[index]);
            }
        }

        var resolved = found.ToArray();
        worldsByRegion[region] = resolved;
        return resolved;
    }

    private static bool ContainsDataCenter(List<DataCenterInfo> dataCenters, uint id)
    {
        for (var index = 0; index < dataCenters.Count; index++)
        {
            if (dataCenters[index].Id == id)
            {
                return true;
            }
        }

        return false;
    }

    // Case-insensitive exact name first, then a prefix that matches exactly one world.
    public static bool TryFind(string name, out WorldInfo world)
    {
        world = default;
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var worlds = All;
        for (var index = 0; index < worlds.Length; index++)
        {
            if (string.Equals(worlds[index].Name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                world = worlds[index];
                return true;
            }
        }

        var matches = 0;
        for (var index = 0; index < worlds.Length; index++)
        {
            if (!worlds[index].Name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matches++;
            if (matches > 1)
            {
                world = default;
                return false;
            }

            world = worlds[index];
        }

        return matches == 1;
    }

    public static bool TryFindById(uint worldId, out WorldInfo world)
    {
        var worlds = All;
        for (var index = 0; index < worlds.Length; index++)
        {
            if (worlds[index].Id != worldId)
            {
                continue;
            }

            world = worlds[index];
            return true;
        }

        world = default;
        return false;
    }

    public static bool TryCurrent(out WorldInfo world)
    {
        if (Svc.Objects.LocalPlayer is { } player)
        {
            return TryFindById(player.CurrentWorld.RowId, out world);
        }

        world = default;
        return false;
    }

    public static bool TryHome(out WorldInfo world)
    {
        if (Svc.Objects.LocalPlayer is { } player)
        {
            return TryFindById(player.HomeWorld.RowId, out world);
        }

        world = default;
        return false;
    }

    public static bool SameDataCenter(in WorldInfo first, in WorldInfo second) => first.DataCenterId == second.DataCenterId;

    public static bool SameRegion(in WorldInfo first, in WorldInfo second) => first.Region == second.Region;

    public static string RegionLabel(WorldRegion region) => region switch
    {
        WorldRegion.Japan => "JP",
        WorldRegion.NorthAmerica => "NA",
        WorldRegion.Europe => "EU",
        WorldRegion.Oceania => "OCE",
        _ => "??",
    };

    private static WorldInfo[] Build()
    {
        var sheet = Svc.Data.GetExcelSheet<World>();
        var found = new List<WorldInfo>(sheet.Count);
        for (var rowIndex = 0; rowIndex < sheet.Count; rowIndex++)
        {
            var row = sheet.GetRowAt(rowIndex);
            if (!row.IsPublic || row.DataCenter.ValueNullable is not { } dataCenter)
            {
                continue;
            }

            var name = row.Name.ExtractText();
            var dataCenterName = dataCenter.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dataCenterName))
            {
                continue;
            }

            if (dataCenter.IsCloud || dataCenterName.Contains(CloudDataCenterMarker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var region = (WorldRegion)dataCenter.Region.RowId;
            if (region is < WorldRegion.Japan or > WorldRegion.Oceania)
            {
                continue;
            }

            found.Add(new WorldInfo(row.RowId, name, dataCenter.RowId, dataCenterName, region));
        }

        return found.ToArray();
    }
}
