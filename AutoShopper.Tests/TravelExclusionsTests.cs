using AutoShopper.Core.Execution.Model;
using static AutoShopper.Tests.Fixtures;

namespace AutoShopper.Tests;

public sealed class TravelExclusionsTests
{
    [Fact]
    public void ARetryableFailureLeavesTheWorldOutAfterTheAttemptBound()
    {
        var exclusions = new TravelExclusions();

        var first = exclusions.Record(Beta, TravelFailure.TimedOut, PrimaryDataCenter);
        var second = exclusions.Record(Beta, TravelFailure.Abandoned, PrimaryDataCenter);

        Assert.Equal(ExclusionStep.Retry, first);
        Assert.Equal(ExclusionStep.Excluded, second);
        Assert.Equal(TravelExclusions.AttemptsPerWorld, exclusions.Failures(Beta.WorldId));
        Assert.Equal(TravelFailure.Abandoned, exclusions.LastFailure(Beta.WorldId));
        Assert.True(exclusions.IsExcluded(Beta));
        Assert.Equal([Beta.WorldId], exclusions.ExcludedWorldIds.AsSpan().ToArray());
    }

    [Fact]
    public void ARefusedOrCongestedWorldIsLeftOutAtOnce()
    {
        var exclusions = new TravelExclusions();

        var refused = exclusions.Record(Gamma, TravelFailure.Refused, PrimaryDataCenter);
        var congested = exclusions.Record(Delta, TravelFailure.Congested, PrimaryDataCenter);

        Assert.Equal(ExclusionStep.Excluded, refused);
        Assert.Equal(ExclusionStep.Excluded, congested);
        Assert.True(exclusions.IsExcluded(Gamma));
        Assert.True(exclusions.IsExcluded(Delta));
        Assert.False(exclusions.IsExcluded(Beta));
    }

    [Fact]
    public void DataCenterTravelOffConfinesTheRunToTheCurrentDataCenter()
    {
        var exclusions = new TravelExclusions();

        var step = exclusions.Record(Delta, TravelFailure.DataCenterTravelOff, PrimaryDataCenter);

        Assert.Equal(ExclusionStep.Excluded, step);
        Assert.Equal(PrimaryDataCenter, exclusions.ConfinedToDataCenterId);
        Assert.True(exclusions.IsExcluded(Delta));
        Assert.True(exclusions.IsExcluded(Epsilon));
        Assert.True(exclusions.IsExcluded(Zeta));
        Assert.False(exclusions.IsExcluded(Beta));
    }

    [Fact]
    public void LifestreamBusyHaltsTravelAfterTheBusyLimitInARow()
    {
        var exclusions = new TravelExclusions();

        var first = exclusions.Record(Beta, TravelFailure.LifestreamBusy, PrimaryDataCenter);
        var second = exclusions.Record(Gamma, TravelFailure.LifestreamBusy, PrimaryDataCenter);

        Assert.Equal(ExclusionStep.Retry, first);
        Assert.Equal(ExclusionStep.TravelHalted, second);
        Assert.True(exclusions.TravelHalted);
        Assert.False(exclusions.IsExcluded(Beta));
    }

    [Fact]
    public void AnArrivalBetweenBusyTripsStartsTheCountAgain()
    {
        var exclusions = new TravelExclusions();

        exclusions.Record(Beta, TravelFailure.LifestreamBusy, PrimaryDataCenter);
        exclusions.RecordArrival();
        var step = exclusions.Record(Gamma, TravelFailure.LifestreamBusy, PrimaryDataCenter);

        Assert.Equal(ExclusionStep.Retry, step);
        Assert.False(exclusions.TravelHalted);
    }

    [Fact]
    public void ABusyTripDoesNotCountAgainstTheWorld()
    {
        var exclusions = new TravelExclusions();

        exclusions.Record(Beta, TravelFailure.LifestreamBusy, PrimaryDataCenter);
        var step = exclusions.Record(Beta, TravelFailure.TimedOut, PrimaryDataCenter);

        Assert.Equal(ExclusionStep.Retry, step);
        Assert.Equal(1, exclusions.Failures(Beta.WorldId));
    }

    [Fact]
    public void RestoredWorldsStayLeftOut()
    {
        var exclusions = new TravelExclusions();

        exclusions.Restore([Gamma.WorldId, Beta.WorldId]);

        Assert.True(exclusions.IsExcluded(Beta));
        Assert.True(exclusions.IsExcluded(Gamma));
        Assert.Equal([Beta.WorldId, Gamma.WorldId], exclusions.ExcludedWorldIds.AsSpan().ToArray());
        Assert.Equal(ExclusionStep.Excluded, exclusions.Record(Beta, TravelFailure.TimedOut, PrimaryDataCenter));
    }

    [Fact]
    public void NoFailureChangesNothing()
    {
        var exclusions = new TravelExclusions();

        var step = exclusions.Record(Beta, TravelFailure.None, PrimaryDataCenter);

        Assert.Equal(ExclusionStep.Retry, step);
        Assert.Equal(0, exclusions.Revision);
        Assert.Equal(0, exclusions.Failures(Beta.WorldId));
    }
}
