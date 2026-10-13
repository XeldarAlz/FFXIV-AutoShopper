using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Pricing.Model;

internal readonly record struct ScopeWorld(uint WorldId, string Name, uint DataCenterId, string DataCenterName, byte RegionId)
{
    public bool IsKnown => WorldId != 0;

    public PlanWorld ToPlanWorld() => new(WorldId, DataCenterId, RegionId, Name);
}
