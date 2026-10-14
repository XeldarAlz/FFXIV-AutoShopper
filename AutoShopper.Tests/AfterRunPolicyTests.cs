using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Tests;

public sealed class AfterRunPolicyTests
{
    [Theory]
    [InlineData(AfterRunAction.Logout)]
    [InlineData(AfterRunAction.CloseGame)]
    [InlineData(AfterRunAction.ReturnToInn)]
    public void ARunTakesItsAfterRunAction(AfterRunAction action)
    {
        var choice = AfterRunPolicy.Decide(Facts(action));

        Assert.True(choice.WayHome);
        Assert.Equal(action, choice.Action);
    }

    [Fact]
    public void AStopAFaultOrALogoutRunsNothing()
    {
        Assert.Equal(AfterRunChoice.Nothing, AfterRunPolicy.Decide(Facts(AfterRunAction.Logout) with { CompletedByStopCondition = false }));
        Assert.Equal(AfterRunChoice.Nothing, AfterRunPolicy.Decide(Facts(AfterRunAction.Logout) with { EndedWithFault = true }));
        Assert.Equal(AfterRunChoice.Nothing, AfterRunPolicy.Decide(Facts(AfterRunAction.Logout) with { LoggedIn = false }));
    }

    [Fact]
    public void TheWayHomeNeedsTheSettingAWorldAwayAndAnAllowedRoute()
    {
        Assert.False(AfterRunPolicy.Decide(Facts(AfterRunAction.StayLoggedIn) with { ReturnHome = false }).WayHome);
        Assert.False(AfterRunPolicy.Decide(Facts(AfterRunAction.StayLoggedIn) with { AwayFromHome = false }).WayHome);
        Assert.False(AfterRunPolicy.Decide(Facts(AfterRunAction.StayLoggedIn) with { WayHomeAllowed = false }).WayHome);
        Assert.True(AfterRunPolicy.Decide(Facts(AfterRunAction.StayLoggedIn)).WayHome);
    }

    [Fact]
    public void ARunThatDidNothingTakesNoAfterRunAction()
    {
        var choice = AfterRunPolicy.Decide(Facts(AfterRunAction.Logout) with { DidWork = false });

        Assert.Null(choice.Action);
        Assert.True(choice.HasWork);
    }

    private static AfterRunFacts Facts(AfterRunAction action) => new()
    {
        CompletedByStopCondition = true,
        EndedWithFault = false,
        LoggedIn = true,
        DidWork = true,
        ReturnHome = true,
        AwayFromHome = true,
        WayHomeAllowed = true,
        Action = action,
    };
}
