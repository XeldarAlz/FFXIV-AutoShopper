using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Execution.Model;

// What the rest of a route may still cost in teleports. Purchases leave that much above the reserve, and a trip that
// could take the purse below the reserve is not started.
internal static class TravelBudget
{
    // On the high side of one teleport fee, which the game sets by the distance travelled.
    public const long TeleportEstimateGil = 1_000;

    // One teleport for each stop still ahead, and one more for the way home when it is due.
    public static long Commitment(int stopsAhead, bool wayHome)
        => ((long)Math.Max(0, stopsAhead) + (wayHome ? 1 : 0)) * TeleportEstimateGil;

    public static bool Affords(long gil, long reserve, long cost)
        => Gil.TrySubtract(gil, Math.Max(0, cost), out var after) && after >= Math.Max(0, reserve);
}
