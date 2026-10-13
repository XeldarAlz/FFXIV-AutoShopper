using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Stats;
using AutoShopper.Core.Travel;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Tasks;

internal readonly record struct RunEnd(RunState State, string Reason);

// The buyer: checks the run, freezes its authorization, then visits the plan's stops and buys at each one, one guarded
// purchase at a time. Every launch, the first one, a Resume and a fault restart, comes back here with the same run
// state; a run already authorized checks itself again and goes on where it stopped.
internal sealed partial class AutoShopRun(ShopRunState run, ShopSession session) : AutoCommon
{
    private const string Scope = "Shop";
    private const int PollFrames = 1;
    private const int SlowPollFrames = 5;
    // Recorded with every authorization; it rises when what an authorization freezes changes meaning.
    private const int PolicyVersion = 1;

    private static Plugin Owner => Plugin.Instance;

    protected override async Task Execute()
    {
        try
        {
            var end = await RunToEnd();
            if (end is { } finished && !CancelToken.IsCancellationRequested)
            {
                Conclude(finished);
            }
        }
        catch (Exception exception)
        {
            session.RecordFault(exception, CancelToken);
            throw;
        }
    }

    private async Task<RunEnd?> RunToEnd()
    {
        var refusal = run.IsAuthorized ? await PrepareAuthorized() : await PrepareFresh();
        if (CancelToken.IsCancellationRequested)
        {
            return null;
        }

        if (refusal is { } refused)
        {
            Refuse(refused);
            return null;
        }

        if (run.HasUnknownOutcome)
        {
            return new RunEnd(RunState.NeedsReconciliation, "a purchase has no clear answer");
        }

        return await ShopStops();
    }

    private void Refuse(PreflightRefusal refusal)
    {
        RunLog.Info($"Run: refused before buying: {refusal.Reason} ({refusal.Code}).");
        PrintError(Loc.T(L.Buyer.ChatRefused, refusal.Reason));
        run.NoteRefusal(refusal.Code, refusal.Reason);
        if (run.IsAuthorized)
        {
            session.Interrupted = true;
            return;
        }

        session.Outcome = RunOutcome.Stopped;
    }

    private void Conclude(RunEnd end)
    {
        var final = run.HasUnknownOutcome ? RunState.NeedsReconciliation : end.State;
        NoteMissingUnits();
        run.Finish(final, end.Reason, DateTime.UtcNow);
        if (!run.DryRun)
        {
            Owner.Ledger.Invalidate();
        }

        run.ApplyTo(session);
        var outcome = ShopRunState.OutcomeOf(final);
        session.Outcome = outcome;
        session.CompletedByStopCondition = final is RunState.Completed or RunState.CompletedPartial;
        RunLog.Info($"Run: ended {final} ({end.Reason}); {session.Units} unit(s) for {session.Spent} gil{(run.DryRun ? " in a dry run" : string.Empty)}.");
        var label = OutcomeLabel(outcome);
        var spent = Loc.Gil(session.Spent);
        var units = session.Units.ToString("N0", Loc.Culture);
        Print(run.DryRun ? Loc.T(L.Buyer.ChatEndedDry, label, spent, units) : Loc.T(L.Buyer.ChatEnded, label, spent, units));
        if (final == RunState.NeedsReconciliation)
        {
            PrintError(Loc.T(L.Buyer.ChatUncertain));
        }
    }

    // A line the run could not finish and nothing else explains is a line nobody listed enough of.
    private void NoteMissingUnits()
    {
        var tally = run.Tally;
        for (var index = 0; index < tally.Count; index++)
        {
            var line = tally.LineAt(index);
            if (!line.Satisfied && !run.HasIssueFor(line.Demand.ItemId))
            {
                run.AddIssue(RunIssueReason.NoStock, line.Demand.ItemId, 0, line.Missing);
            }
        }
    }

    // Polls work started on the thread pool once a frame, so everything after it runs on the framework thread again.
    // The linked source is disposed once the work is over, which a cancelled run learns of only later.
    private async Task<T?> OffThread<T>(Func<CancellationToken, T> work, string label)
        where T : class
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancelToken);
        var token = cancellation.Token;
        var task = Task.Run(() => work(token), token);
        try
        {
            while (!task.IsCompleted)
            {
                await NextFrame(PollFrames);
            }
        }
        finally
        {
            if (task.IsCompleted)
            {
                cancellation.Dispose();
            }
            else
            {
                cancellation.Cancel();
                _ = task.ContinueWith(static (_, source) => ((CancellationTokenSource)source!).Dispose(), cancellation, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        if (task.IsCompletedSuccessfully)
        {
            return task.Result;
        }

        if (task.Exception is { } failure)
        {
            RunLog.Error(failure.GetBaseException(), $"Run: {label} failed");
        }

        return null;
    }

    private void NoteWorld()
    {
        if (!Worlds.TryCurrent(out var world))
        {
            return;
        }

        session.WorldName = world.Name;
        session.DataCenterName = world.DataCenterName;
    }

    private static string ItemName(uint itemId)
        => Owner.Items.TryGetName(itemId, out var name) ? name : itemId.ToString(CultureInfo.InvariantCulture);

    internal static string OutcomeLabel(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Completed => Loc.T(L.History.OutcomeCompleted),
        RunOutcome.CompletedPartial => Loc.T(L.History.OutcomeCompletedPartial),
        RunOutcome.NeedsReconciliation => Loc.T(L.History.OutcomeNeedsReconciliation),
        RunOutcome.Failed => Loc.T(L.History.OutcomeFailed),
        _ => Loc.T(L.History.OutcomeStopped),
    };

    private static void Print(string message) => Svc.Chat.Print($"{AshopConstants.LogPrefix} {message}");

    private static void PrintError(string message) => Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {message}");
}
