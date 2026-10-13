using System.Collections.Immutable;

namespace AutoShopper.Core.Pricing.Model;

internal enum PriceQueryKind : byte
{
    World,
    DataCenter,
    Region,
}

// One price source scope and the allowed worlds its answer is filtered to.
internal sealed record PriceQuery(PriceQueryKind Kind, string Segment, ImmutableArray<ScopeWorld> Worlds)
{
    public int IndexOfWorld(uint worldId)
    {
        for (var index = 0; index < Worlds.Length; index++)
        {
            if (Worlds[index].WorldId == worldId)
            {
                return index;
            }
        }

        return -1;
    }
}

internal sealed record PriceChunk(PriceQuery Query, ImmutableArray<uint> ItemIds);
