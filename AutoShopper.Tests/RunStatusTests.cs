using AutoShopper.Core.Api.Model;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Shopping.Model;
using AutoShopper.Core.Stats;
using AutoShopper.Core.Tasks;

namespace AutoShopper.Tests;

public sealed class RunStatusTests
{
    [Fact]
    public void TheFinalResultCarriesARevisionAboveTheLastLiveStatus()
    {
        var live = Live();

        var final = live.AsTerminal(RunState.Completed, "Completed", RunOutcome.Completed, needsPlayer: false);

        Assert.True(final.Revision > live.Revision);
        Assert.Equal(live.Revision + 1, final.Revision);
    }

    [Fact]
    public void AFinalResultThatChangesNothingElseStillMovesTheRevision()
    {
        var live = Live() with { Phase = RunPhase.Idle, State = RunState.Stopped, Status = "Stopped", Paused = false, PauseReason = null };

        var final = live.AsTerminal(RunState.Stopped, "Stopped", RunOutcome.Stopped, needsPlayer: false);

        Assert.NotEqual(live.Revision, final.Revision);
        Assert.True(final.Terminal);
    }

    [Fact]
    public void TheFinalResultIsIdleUnpausedAndCarriesTheOutcome()
    {
        var live = Live();

        var final = live.AsTerminal(RunState.NeedsReconciliation, "Needs your answer", RunOutcome.NeedsReconciliation, needsPlayer: true);

        Assert.Equal(RunPhase.Idle, final.Phase);
        Assert.Equal(RunState.NeedsReconciliation, final.State);
        Assert.Equal("Needs your answer", final.Status);
        Assert.False(final.Paused);
        Assert.Null(final.PauseReason);
        Assert.True(final.Terminal);
        Assert.Equal(RunOutcome.NeedsReconciliation, final.Outcome);
        Assert.True(final.NeedsPlayer);
    }

    [Fact]
    public void TheFinalResultKeepsWhatTheRunBoughtAndWhoAskedForIt()
    {
        var live = Live();

        var final = live.AsTerminal(RunState.Completed, "Completed", RunOutcome.Completed, needsPlayer: false);

        Assert.Equal(live.RunId, final.RunId);
        Assert.True(final.Known);
        Assert.Equal(live.CallerRunId, final.CallerRunId);
        Assert.Equal(live.ConfirmedSpend, final.ConfirmedSpend);
        Assert.Same(live.Lines, final.Lines);
        Assert.Same(live.Issues, final.Issues);
        Assert.Equal(live.ReturnHome, final.ReturnHome);
    }

    private static RunStatusResponse Live() => new()
    {
        RunId = "4e1a0b3c-2d5f-4a6b-8c7d-9e0f1a2b3c4d",
        Known = true,
        CallerRunId = "0d7f",
        Phase = RunPhase.Paused,
        State = RunState.Paused,
        Status = "Paused",
        Revision = 42,
        Paused = true,
        PauseReason = PauseReason.Manual,
        ConfirmedSpend = 1_890,
        Lines = [new RunStatusLine(5_057, ItemQuality.NormalQuality, 3, 3, 0)],
        Issues = [new RunStatusIssue(RunIssueReason.NoStock, 5_058)],
        ReturnHome = ApiReturnHome.Arrived,
    };
}
