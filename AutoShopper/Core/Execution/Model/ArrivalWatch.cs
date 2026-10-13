namespace AutoShopper.Core.Execution.Model;

// An arrival counts once the character stands on the target world, out of any loading screen, with Lifestream idle for
// the whole settle window. Lifestream's busy flag turning false proves nothing on its own: it also turns false when
// Lifestream gives a trip up.
internal struct ArrivalWatch
{
    private long quietSinceMs;
    private bool quiet;

    public bool Observe(long nowMs, bool onTargetWorld, bool betweenAreas, bool lifestreamBusy, long settleMs)
    {
        if (!onTargetWorld || betweenAreas || lifestreamBusy)
        {
            quiet = false;
            return false;
        }

        if (!quiet)
        {
            quiet = true;
            quietSinceMs = nowMs;
        }

        return nowMs - quietSinceMs >= settleMs;
    }
}
