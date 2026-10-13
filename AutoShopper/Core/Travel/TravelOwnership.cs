using AutoShopper.Core.Ipc;

namespace AutoShopper.Core.Travel;

// Which task started the world change Lifestream is working on. Lifestream's Abort stops whatever it is doing, work
// another plugin or the player started included, so only the owner of the operation may call it. A hop is claimed only
// when Lifestream was idle right before the request and busy right after it. Framework thread only.
internal static class TravelOwnership
{
    private static object? owner;

    public static void Claim(object claimant)
    {
        owner = claimant;
        RunLog.Debug($"Travel: the world change under way is owned by {claimant.GetType().Name}.");
    }

    public static void Release(object claimant)
    {
        if (ReferenceEquals(owner, claimant))
        {
            owner = null;
        }
    }

    public static bool IsOwnedBy(object? claimant) => claimant is not null && ReferenceEquals(owner, claimant);

    // True when Lifestream was told to stop this owner's operation.
    public static bool AbortIfOwnedBy(object? claimant, string reason)
    {
        if (!IsOwnedBy(claimant))
        {
            return false;
        }

        owner = null;
        var lifestream = LifestreamIPC.Instance;
        if (!lifestream.IsBusy())
        {
            return false;
        }

        lifestream.Abort();
        RunLog.Info($"Travel: {reason}; Lifestream's world change, started by this run, was aborted.");
        return true;
    }
}
