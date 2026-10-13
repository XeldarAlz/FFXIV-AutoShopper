using System.Collections.Immutable;
using System.Globalization;
using AutoShopper.Core.Pricing.Model;

namespace AutoShopper.Core.Pricing.Universalis;

// One world is asked for by id, worlds of one data center by its name, anything wider by the region; the answer is
// then filtered to the allowed worlds.
internal static class UniversalisScope
{
    public static ImmutableArray<PriceQuery> Plan(ImmutableArray<ScopeWorld> worlds)
    {
        var queries = ImmutableArray.CreateBuilder<PriceQuery>();
        var regions = new List<byte>();
        for (var index = 0; index < worlds.Length; index++)
        {
            if (worlds[index].IsKnown && !regions.Contains(worlds[index].RegionId))
            {
                regions.Add(worlds[index].RegionId);
            }
        }

        for (var regionIndex = 0; regionIndex < regions.Count; regionIndex++)
        {
            var regionWorlds = WorldsWhere(worlds, regions[regionIndex], dataCenterId: null);
            AddRegion(queries, regions[regionIndex], regionWorlds);
        }

        return queries.ToImmutable();
    }

    public static ImmutableArray<PriceChunk> Chunk(ImmutableArray<PriceQuery> queries, ReadOnlySpan<uint> itemIds)
    {
        var ids = DistinctSorted(itemIds);
        var chunks = ImmutableArray.CreateBuilder<PriceChunk>();
        if (ids.Length == 0)
        {
            return chunks.ToImmutable();
        }

        for (var queryIndex = 0; queryIndex < queries.Length; queryIndex++)
        {
            var query = queries[queryIndex];
            var size = UniversalisApi.IdsPerRequest(query.Kind);
            for (var start = 0; start < ids.Length; start += size)
            {
                var length = Math.Min(size, ids.Length - start);
                chunks.Add(new PriceChunk(query, ImmutableArray.Create(ids, start, length)));
            }
        }

        return chunks.ToImmutable();
    }

    private static void AddRegion(ImmutableArray<PriceQuery>.Builder queries, byte regionId, ImmutableArray<ScopeWorld> regionWorlds)
    {
        if (regionWorlds.Length == 1)
        {
            queries.Add(WorldQuery(regionWorlds[0]));
            return;
        }

        if (SingleDataCenter(regionWorlds))
        {
            queries.Add(new PriceQuery(PriceQueryKind.DataCenter, regionWorlds[0].DataCenterName, regionWorlds));
            return;
        }

        if (UniversalisApi.RegionSegment(regionId) is { } regionName)
        {
            queries.Add(new PriceQuery(PriceQueryKind.Region, regionName, regionWorlds));
            return;
        }

        var dataCenters = new List<uint>();
        for (var index = 0; index < regionWorlds.Length; index++)
        {
            if (!dataCenters.Contains(regionWorlds[index].DataCenterId))
            {
                dataCenters.Add(regionWorlds[index].DataCenterId);
            }
        }

        for (var index = 0; index < dataCenters.Count; index++)
        {
            var dataCenterWorlds = WorldsWhere(regionWorlds, regionId, dataCenters[index]);
            queries.Add(dataCenterWorlds.Length == 1
                ? WorldQuery(dataCenterWorlds[0])
                : new PriceQuery(PriceQueryKind.DataCenter, dataCenterWorlds[0].DataCenterName, dataCenterWorlds));
        }
    }

    private static PriceQuery WorldQuery(ScopeWorld world)
        => new(PriceQueryKind.World, world.WorldId.ToString(CultureInfo.InvariantCulture), [world]);

    private static bool SingleDataCenter(ImmutableArray<ScopeWorld> worlds)
    {
        for (var index = 1; index < worlds.Length; index++)
        {
            if (worlds[index].DataCenterId != worlds[0].DataCenterId)
            {
                return false;
            }
        }

        return true;
    }

    private static ImmutableArray<ScopeWorld> WorldsWhere(ImmutableArray<ScopeWorld> worlds, byte regionId, uint? dataCenterId)
    {
        var matching = ImmutableArray.CreateBuilder<ScopeWorld>();
        for (var index = 0; index < worlds.Length; index++)
        {
            var world = worlds[index];
            if (!world.IsKnown || world.RegionId != regionId || (dataCenterId is { } id && world.DataCenterId != id))
            {
                continue;
            }

            matching.Add(world);
        }

        return matching.ToImmutable();
    }

    private static uint[] DistinctSorted(ReadOnlySpan<uint> itemIds)
    {
        var sorted = itemIds.ToArray();
        Array.Sort(sorted);
        var write = 0;
        for (var read = 0; read < sorted.Length; read++)
        {
            var itemId = sorted[read];
            if (itemId == 0 || (write > 0 && sorted[write - 1] == itemId))
            {
                continue;
            }

            sorted[write] = itemId;
            write++;
        }

        Array.Resize(ref sorted, write);
        return sorted;
    }
}
