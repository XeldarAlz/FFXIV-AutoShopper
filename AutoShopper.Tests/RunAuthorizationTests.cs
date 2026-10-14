using System.Collections.Immutable;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.ExecutionFixtures;

namespace AutoShopper.Tests;

public sealed class RunAuthorizationTests
{
    [Fact]
    public void AuthorizationFreezesASortedCopyOfDemandAndWorlds()
    {
        var authorization = Authorization();

        Assert.Equal([OreDemand, ClothDemand], authorization.Demands.AsSpan().ToArray());
        Assert.Equal([HomeWorld, OtherAllowedWorld], authorization.AllowedWorldIds.AsSpan().ToArray());
        Assert.True(authorization.AllowsWorld(OtherAllowedWorld));
        Assert.False(authorization.AllowsWorld(99));
        Assert.True(authorization.TryGetDemand(ClothDemand.Key, out var cloth));
        Assert.Equal(ClothDemand, cloth);
    }

    [Fact]
    public void InventoryTargetShortfallIsFrozenAtAuthorization()
    {
        var shortfall = PlanDemand.ForInventoryTarget(Ore, DemandQuality.Any, 30, 12);
        Create(out var authorization, demands: [shortfall]);

        var laterConsumption = PlanDemand.ForInventoryTarget(Ore, DemandQuality.Any, 30, 2);

        Assert.True(authorization!.TryGetDemand(shortfall.Key, out var frozen));
        Assert.Equal(18, frozen.RequiredQuantity);
        Assert.Equal(28, laterConsumption.RequiredQuantity);
        Assert.Equal(18, frozen.RequiredQuantity);
    }

    [Fact]
    public void StartIsRefusedWithoutASpendCap()
    {
        Assert.Equal(AuthorizationProblem.MissingSpendCap, Create(out var authorization, maxSpend: 0));
        Assert.Null(authorization);
    }

    [Fact]
    public void EveryAuthorizationProblemIsDetected()
    {
        Assert.Equal(AuthorizationProblem.MissingRunId, Create(out _, runId: Guid.Empty));
        Assert.Equal(AuthorizationProblem.MissingCharacter, Create(out _, character: 0));
        Assert.Equal(AuthorizationProblem.MissingHomeWorld, Create(out _, homeWorld: 0));
        Assert.Equal(AuthorizationProblem.NegativeReserve, Create(out _, reserve: -1));
        Assert.Equal(AuthorizationProblem.NoAllowedWorlds, Create(out _, worlds: []));
        Assert.Equal(AuthorizationProblem.NoDemand, Create(out _, demands: [PlanDemand.ForPurchase(Ore, DemandQuality.Any, 0)]));
        Assert.Equal(AuthorizationProblem.InvalidDemand, Create(out _, demands: [PlanDemand.ForPurchase(Ore, DemandQuality.Any, -1)]));
        Assert.Equal(AuthorizationProblem.DuplicateDemand, Create(out _, demands: [OreDemand, OreDemand]));
        Assert.Equal(AuthorizationProblem.None, Create(out _));
    }

    [Fact]
    public void JournalEventRoundTripsTheAuthorization()
    {
        var authorization = Authorization();

        var recorded = authorization.ToJournalEvent(1, Now);
        Assert.Equal(AuthorizationProblem.None, RunAuthorization.TryRestore(recorded, out var restored));

        Assert.Equal(authorization.RunId, restored!.RunId);
        Assert.Equal(authorization.Demands.AsSpan().ToArray(), restored.Demands.AsSpan().ToArray());
        Assert.Equal(authorization.AllowedWorldIds.AsSpan().ToArray(), restored.AllowedWorldIds.AsSpan().ToArray());
        Assert.Equal(authorization.MaxSpend, restored.MaxSpend);
        Assert.Equal(authorization.GilReserve, restored.GilReserve);
        Assert.Equal(authorization.CreatedAtUtc, restored.CreatedAtUtc);
    }

    [Fact]
    public void TheStopAndRevisitLimitsTravelWithTheAuthorization()
    {
        RunAuthorization.TryCreate(RunId, Character, HomeWorld, [OreDemand], [HomeWorld], MaxSpend, Reserve, 1, Now, out var authorization, maxStops: 4, worldRevisits: 2);

        Assert.Equal(AuthorizationProblem.None, RunAuthorization.TryRestore(authorization!.ToJournalEvent(1, Now), out var restored));

        Assert.Equal(4, restored!.MaxStops);
        Assert.Equal(2, restored.WorldRevisits);
    }

    [Fact]
    public void AJournalFromBeforeTheLimitsWereRecordedLeavesThemUnknown()
    {
        var recorded = Authorization().ToJournalEvent(1, Now) with { MaxStops = null, WorldRevisits = null };

        Assert.Equal(AuthorizationProblem.None, RunAuthorization.TryRestore(recorded, out var restored));

        Assert.Null(restored!.MaxStops);
        Assert.Null(restored.WorldRevisits);
    }

    [Fact]
    public void TheCallerOfAPluginsRunTravelsWithTheAuthorization()
    {
        var caller = new RunCaller("AutoGcSupply", "Auto GC Supply", ReturnHome: true);
        RunAuthorization.TryCreate(RunId, Character, HomeWorld, [OreDemand], [HomeWorld], MaxSpend, Reserve, 1, Now, out var authorization, caller: caller);

        var recorded = authorization!.ToJournalEvent(1, Now);

        Assert.Equal(caller, recorded.Caller);
        Assert.Equal(AuthorizationProblem.None, RunAuthorization.TryRestore(recorded, out var restored));
        Assert.Equal(caller, restored!.Caller);
    }

    [Fact]
    public void ThePlayersOwnRunHasNoCaller()
    {
        var recorded = Authorization().ToJournalEvent(1, Now);

        Assert.Null(recorded.Caller);
        Assert.Equal(AuthorizationProblem.None, RunAuthorization.TryRestore(recorded, out var restored));
        Assert.Null(restored!.Caller);
    }

    private static AuthorizationProblem Create(
        out RunAuthorization? authorization,
        Guid? runId = null,
        ulong character = Character,
        uint homeWorld = HomeWorld,
        long maxSpend = MaxSpend,
        long reserve = Reserve,
        ImmutableArray<uint>? worlds = null,
        ImmutableArray<PlanDemand>? demands = null)
    {
        return RunAuthorization.TryCreate(
            runId ?? RunId,
            character,
            homeWorld,
            demands ?? [OreDemand],
            worlds ?? [HomeWorld],
            maxSpend,
            reserve,
            1,
            Now,
            out authorization);
    }
}
