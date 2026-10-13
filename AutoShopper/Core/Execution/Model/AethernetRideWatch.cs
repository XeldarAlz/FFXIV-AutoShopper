namespace AutoShopper.Core.Execution.Model;

internal enum RideProgress : byte { Underway, Arrived, Abandoned }

// Lifestream's busy flag turns false as soon as its last menu click lands, while the hop's load and fade are still
// ahead, so idle time counts only while the character is ready and the screen is up. An idle stretch of abandonedIdleMs
// with the stop still out of reach is a ride Lifestream gave up; a ride that never showed busy is left to the caller's
// deadline, because nothing says it was ever taken.
internal struct AethernetRideWatch
{
    private long idleSinceMs;
    private bool sawBusy;
    private bool idle;

    public RideProgress Observe(long nowMs, bool lifestreamBusy, bool settled, bool atStop, long abandonedIdleMs)
    {
        if (lifestreamBusy)
        {
            sawBusy = true;
            idle = false;
            return RideProgress.Underway;
        }

        if (!settled)
        {
            idle = false;
            return RideProgress.Underway;
        }

        if (atStop)
        {
            return RideProgress.Arrived;
        }

        if (!sawBusy)
        {
            return RideProgress.Underway;
        }

        if (!idle)
        {
            idle = true;
            idleSinceMs = nowMs;
        }

        return nowMs - idleSinceMs >= abandonedIdleMs ? RideProgress.Abandoned : RideProgress.Underway;
    }
}
