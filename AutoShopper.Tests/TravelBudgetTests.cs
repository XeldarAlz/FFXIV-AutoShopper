using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Tests;

public sealed class TravelBudgetTests
{
    [Fact]
    public void EveryStopAheadAndTheWayHomeAreKeptAside()
    {
        Assert.Equal(0, TravelBudget.Commitment(0, wayHome: false));
        Assert.Equal(TravelBudget.TeleportEstimateGil, TravelBudget.Commitment(0, wayHome: true));
        Assert.Equal(3 * TravelBudget.TeleportEstimateGil, TravelBudget.Commitment(2, wayHome: true));
        Assert.Equal(0, TravelBudget.Commitment(-4, wayHome: false));
    }

    [Fact]
    public void ATripMayNotTakeThePurseBelowTheReserve()
    {
        const long reserve = 10_000;

        Assert.True(TravelBudget.Affords(reserve + TravelBudget.TeleportEstimateGil, reserve, TravelBudget.TeleportEstimateGil));
        Assert.False(TravelBudget.Affords(reserve + TravelBudget.TeleportEstimateGil - 1, reserve, TravelBudget.TeleportEstimateGil));
        Assert.False(TravelBudget.Affords(500, 0, TravelBudget.TeleportEstimateGil));
    }

    [Fact]
    public void TheCommitmentCountsAgainstTheReserveInTheGuard()
    {
        var input = ExecutionFixtures.ValidInput() with { CurrentGil = 20_000, TravelCommitment = TravelBudget.Commitment(5, wayHome: true) };

        Assert.Equal(GuardRejection.ReserveBreached, PurchaseGuard.Evaluate(input).Rejection);
        Assert.True(PurchaseGuard.Evaluate(input with { TravelCommitment = TravelBudget.Commitment(0, wayHome: false) }).IsAllowed);
    }
}
