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

    [Fact]
    public void StayingPutWithoutChatCommandsHasNoAfterRunStep()
    {
        var choice = AfterRunPolicy.Decide(Facts(AfterRunAction.StayLoggedIn) with { ReturnHome = false });

        Assert.Equal(AfterRunChoice.Nothing, choice);
        Assert.False(choice.HasWork);
    }

    [Fact]
    public void ChatCommandsAloneStartTheAfterRunStep()
    {
        var choice = AfterRunPolicy.Decide(Facts(AfterRunAction.StayLoggedIn) with { ReturnHome = false, HasChatCommands = true });

        Assert.False(choice.WayHome);
        Assert.Equal(AfterRunAction.StayLoggedIn, choice.Action);
        Assert.True(choice.ChatCommands);
        Assert.True(choice.HasWork);
    }

    [Theory]
    [InlineData(AfterRunAction.Logout)]
    [InlineData(AfterRunAction.CloseGame)]
    [InlineData(AfterRunAction.ReturnToInn)]
    public void ChatCommandsRideAlongWithTheAfterRunAction(AfterRunAction action)
    {
        var choice = AfterRunPolicy.Decide(Facts(action) with { HasChatCommands = true });

        Assert.True(choice.WayHome);
        Assert.Equal(action, choice.Action);
        Assert.True(choice.ChatCommands);
    }

    [Fact]
    public void AStopAFaultOrALogoutSendsNoChatCommands()
    {
        var facts = Facts(AfterRunAction.StayLoggedIn) with { HasChatCommands = true };

        Assert.Equal(AfterRunChoice.Nothing, AfterRunPolicy.Decide(facts with { CompletedByStopCondition = false }));
        Assert.Equal(AfterRunChoice.Nothing, AfterRunPolicy.Decide(facts with { EndedWithFault = true }));
        Assert.Equal(AfterRunChoice.Nothing, AfterRunPolicy.Decide(facts with { LoggedIn = false }));
    }

    [Fact]
    public void ARunThatDidNothingSendsNoChatCommands()
    {
        var choice = AfterRunPolicy.Decide(Facts(AfterRunAction.Logout) with { DidWork = false, HasChatCommands = true });

        Assert.Null(choice.Action);
        Assert.False(choice.ChatCommands);
        Assert.True(choice.WayHome);
    }

    [Fact]
    public void WithoutChatCommandsTheActionGoesAlone()
    {
        var choice = AfterRunPolicy.Decide(Facts(AfterRunAction.Logout));

        Assert.Equal(AfterRunAction.Logout, choice.Action);
        Assert.False(choice.ChatCommands);
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
        HasChatCommands = false,
    };
}
