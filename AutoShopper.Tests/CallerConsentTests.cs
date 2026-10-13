using AutoShopper.Core.Api.Model;

namespace AutoShopper.Tests;

public sealed class CallerConsentTests
{
    private const string Caller = "AutoGcSupply";
    private const string DisplayName = "Auto GC Supply";
    private const long MaxSpend = 150_000;

    [Fact]
    public void ACallerNobodyAnsweredIsUnknown()
    {
        var consents = new CallerConsents([]);

        Assert.Equal(ConsentState.Unknown, consents.StateOf(Caller));
        Assert.Empty(consents.Pending);
    }

    [Fact]
    public void AskingQueuesTheCallerOnce()
    {
        var consents = new CallerConsents([]);

        consents.Ask(Caller, DisplayName, MaxSpend);
        consents.Ask(Caller, DisplayName, MaxSpend);

        Assert.Equal([new ConsentRequest(Caller, DisplayName, MaxSpend)], consents.Pending);
        Assert.Equal(ConsentState.Unknown, consents.StateOf(Caller));
    }

    [Fact]
    public void AskingAgainUnderANewNameUpdatesTheQuestion()
    {
        var consents = new CallerConsents([]);
        consents.Ask(Caller, DisplayName, MaxSpend);
        var revision = consents.Revision;

        consents.Ask(Caller, "GC Supply", MaxSpend);

        Assert.Equal([new ConsentRequest(Caller, "GC Supply", MaxSpend)], consents.Pending);
        Assert.True(consents.Revision > revision);
    }

    [Fact]
    public void TheQuestionShowsTheCapOfTheNewestRequest()
    {
        var consents = new CallerConsents([]);
        consents.Ask(Caller, DisplayName, MaxSpend);
        var revision = consents.Revision;

        consents.Ask(Caller, DisplayName, 900_000);

        Assert.Equal([new ConsentRequest(Caller, DisplayName, 900_000)], consents.Pending);
        Assert.True(consents.Revision > revision);
    }

    [Fact]
    public void AtMostEightCallersWaitForAnAnswerAndTheOldestMakesWay()
    {
        var consents = new CallerConsents([]);
        for (var index = 0; index <= ApiContract.PendingConsentCapacity; index++)
        {
            consents.Ask($"Caller{index}", $"Caller {index}", MaxSpend);
        }

        Assert.Equal(ApiContract.PendingConsentCapacity, consents.Pending.Count);
        Assert.Equal("Caller1", consents.Pending[0].Caller);
        Assert.Equal($"Caller{ApiContract.PendingConsentCapacity}", consents.Pending[^1].Caller);
    }

    [Fact]
    public void ACallerAlreadyWaitingAsksAgainWithoutPushingAnotherOut()
    {
        var consents = new CallerConsents([]);
        for (var index = 0; index < ApiContract.PendingConsentCapacity; index++)
        {
            consents.Ask($"Caller{index}", $"Caller {index}", MaxSpend);
        }

        consents.Ask("Caller0", "Caller zero", MaxSpend);

        Assert.Equal(ApiContract.PendingConsentCapacity, consents.Pending.Count);
        Assert.Equal(new ConsentRequest("Caller0", "Caller zero", MaxSpend), consents.Pending[0]);
    }

    [Fact]
    public void AllowStoresTheAnswerAndClearsTheQuestion()
    {
        var answers = new List<CallerConsent>();
        var consents = new CallerConsents(answers);
        consents.Ask(Caller, DisplayName, MaxSpend);

        consents.Allow(Caller, DisplayName);

        Assert.Equal(ConsentState.Allowed, consents.StateOf(Caller));
        Assert.Empty(consents.Pending);
        var answer = Assert.Single(answers);
        Assert.Equal(Caller, answer.Caller);
        Assert.Equal(DisplayName, answer.DisplayName);
        Assert.True(answer.Allowed);
    }

    [Fact]
    public void DenyStoresTheAnswerAndADeniedCallerAskingAgainIsNotQueued()
    {
        var consents = new CallerConsents([]);
        consents.Ask(Caller, DisplayName, MaxSpend);

        consents.Deny(Caller, DisplayName);
        consents.Ask(Caller, DisplayName, MaxSpend);

        Assert.Equal(ConsentState.Denied, consents.StateOf(Caller));
        Assert.Empty(consents.Pending);
    }

    [Fact]
    public void AllowingADeniedCallerTurnsItsAnswerAround()
    {
        var answers = new List<CallerConsent>();
        var consents = new CallerConsents(answers);
        consents.Deny(Caller, DisplayName);

        consents.Allow(Caller, DisplayName);

        Assert.Equal(ConsentState.Allowed, consents.StateOf(Caller));
        Assert.Single(answers);
    }

    [Fact]
    public void RevokeForgetsTheAnswerSoTheCallerAsksAgain()
    {
        var answers = new List<CallerConsent>();
        var consents = new CallerConsents(answers);
        consents.Allow(Caller, DisplayName);

        Assert.True(consents.Revoke(Caller));
        Assert.False(consents.Revoke(Caller));

        Assert.Equal(ConsentState.Unknown, consents.StateOf(Caller));
        Assert.Empty(answers);
        consents.Ask(Caller, DisplayName, MaxSpend);
        Assert.Single(consents.Pending);
    }

    [Fact]
    public void CallersAreToldApartByTheirExactName()
    {
        var consents = new CallerConsents([]);

        consents.Allow(Caller, DisplayName);

        Assert.Equal(ConsentState.Unknown, consents.StateOf("autogcsupply"));
    }

    [Fact]
    public void RepairDropsBrokenAndRepeatedAnswers()
    {
        var answers = new List<CallerConsent>
        {
            new() { Caller = Caller, DisplayName = DisplayName, Allowed = true },
            null!,
            new() { Caller = "has space", DisplayName = "x", Allowed = true },
            new() { Caller = string.Empty, Allowed = true },
            new() { Caller = Caller, DisplayName = "Copy", Allowed = false },
            new() { Caller = "Other", DisplayName = null!, Allowed = false },
        };

        Assert.True(CallerConsents.Repair(answers));

        Assert.Equal(2, answers.Count);
        Assert.Equal(DisplayName, answers[0].DisplayName);
        Assert.True(answers[0].Allowed);
        Assert.Equal("Other", answers[1].DisplayName);
        Assert.False(CallerConsents.Repair(answers));
    }
}
