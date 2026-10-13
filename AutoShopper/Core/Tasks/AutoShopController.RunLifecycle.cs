using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Stats;
using AutoShopper.Core.Travel;

namespace AutoShopper.Core.Tasks;

internal sealed partial class AutoShopController
{
    private const int MaxFaultResumes = 3;
    private const int MaxFaultResumesPerRun = 6;
    private const long FaultResumeWindowMs = 5 * TimeUnits.MillisecondsPerMinute;

    private int faultResumeCount;
    private int runFaultResumeCount;
    private long faultWindowStartedAtMs;

    private void OnRunEnded(ShopSession owningSession)
    {
        var faulted = ReferenceEquals(session, owningSession) && owningSession.EndedWithFault;
        if (faulted && !owningSession.CompletedByStopCondition && TryAutoResumeAfterFault(owningSession))
        {
            return;
        }

        if (faulted)
        {
            ECommons.DalamudServices.Svc.Chat.PrintError($"{AshopConstants.LogPrefix} The run stopped on an unexpected error. The log has the details.");
        }

        EndRun(owningSession);
    }

    private void EndRun(ShopSession owningSession)
    {
        var live = ReferenceEquals(session, owningSession);
        if (live && owningSession.Outcome is null)
        {
            EndShopRun(owningSession, owningSession.EndedWithFault ? RunState.Failed : RunState.Stopped, owningSession.EndedWithFault ? "an unexpected error" : "the run task ended");
        }

        // Chosen before the history row is written, so the row says whether the way home was due.
        var choice = live ? ChooseAfterRun(owningSession) : AfterRunChoice.Nothing;
        FinalizeRun(owningSession);
        if (!live)
        {
            Diag("A run that is no longer live ended; it was recorded and the current run is left alone.");
            return;
        }

        if (!choice.HasWork)
        {
            ClearRun();
            return;
        }

        progress.SetPhase(RunPhase.Finishing);
        RunChain(choice.WayHome, choice.Action);
    }

    // One ordered chain once a run ends on its own: the way home, then the after-run action. A Stop or a fault runs
    // neither. The finished run stays on screen while the chain runs, and is cleared once it ends. The way home's
    // result goes to the run's panel and history row, apart from how the shopping ended.
    private AfterRunChoice ChooseAfterRun(ShopSession ending)
    {
        if (ending.AfterActionDispatched)
        {
            return AfterRunChoice.Nothing;
        }

        ending.AfterActionDispatched = true;
        var configuration = Plugin.Instance.Configuration;
        var returnHome = shopRun?.ReturnsHome(configuration) ?? configuration.ReturnHomeAfterRun;
        var afterRun = shopRun?.External is null ? configuration.AfterRun : AfterRunAction.StayLoggedIn;
        var homeKnown = Worlds.TryHome(out var home);
        var currentKnown = Worlds.TryCurrent(out var current);
        var away = homeKnown && currentKnown && home.Id != current.Id;
        // Travel never leaves the region the character stands in, so a home world outside it cannot be reached.
        var sameRegion = !away || Worlds.SameRegion(home, current);
        var wayHomeAllowed = !away || (sameRegion && WayHomeAllowed(home, current, configuration.AllowJapaneseDataCenters));
        var choice = AfterRunPolicy.Decide(new AfterRunFacts
        {
            CompletedByStopCondition = ending.CompletedByStopCondition,
            EndedWithFault = ending.EndedWithFault,
            LoggedIn = ECommons.DalamudServices.Svc.ClientState.IsLoggedIn,
            DidWork = ending.DidWork,
            DryRun = ending.DryRun,
            ReturnHome = returnHome,
            AwayFromHome = away,
            WayHomeAllowed = wayHomeAllowed,
            Action = afterRun,
        });
        Diag($"After the run: way home {choice.WayHome}, after-run action {(choice.Action?.ToString() ?? "none")}{(choice.ActionSkippedForDryRun ? $" ({afterRun} skipped for a dry run)" : string.Empty)}; stop condition {ending.CompletedByStopCondition}, fault {ending.EndedWithFault}, away from home {away}.");
        if (!ending.CompletedByStopCondition || ending.EndedWithFault)
        {
            return choice;
        }

        SetReturnHome(ending, ReturnHomeFor(returnHome, choice.WayHome, homeKnown && currentKnown, away, wayHomeAllowed, sameRegion));
        if (returnHome && away && !sameRegion)
        {
            ECommons.DalamudServices.Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {Loc.T(L.Route.ChatHomeOtherRegion, current.Name)}");
        }
        else if (returnHome && away && !wayHomeAllowed)
        {
            ECommons.DalamudServices.Svc.Chat.Print($"{AshopConstants.LogPrefix} {Loc.T(L.Route.ChatHomeSkippedJapan, current.Name)}");
        }

        if (choice.ActionSkippedForDryRun)
        {
            ECommons.DalamudServices.Svc.Chat.Print($"{AshopConstants.LogPrefix} {Loc.T(L.Route.ChatAfterRunDry, ActionName(afterRun))}");
        }

        return choice;
    }

    private static ReturnHomeResult ReturnHomeFor(bool wanted, bool travelling, bool worldsKnown, bool away, bool allowed, bool sameRegion)
    {
        if (!wanted)
        {
            return ReturnHomeResult.NotAsked;
        }

        if (travelling)
        {
            return ReturnHomeResult.Underway;
        }

        if (!worldsKnown)
        {
            return ReturnHomeResult.Failed;
        }

        if (!away)
        {
            return ReturnHomeResult.AlreadyHome;
        }

        return allowed || !sameRegion ? ReturnHomeResult.Failed : ReturnHomeResult.Skipped;
    }

    private static string ActionName(AfterRunAction action) => action switch
    {
        AfterRunAction.Logout => Loc.T(L.Travel.AfterLogoutName),
        AfterRunAction.CloseGame => Loc.T(L.Travel.AfterCloseName),
        AfterRunAction.ReturnToInn => Loc.T(L.Travel.AfterInnName),
        _ => Loc.T(L.Travel.AfterStayName),
    };

    // Travel between Japanese data centers stays off with the setting, the way home included.
    private static bool WayHomeAllowed(in WorldInfo home, in WorldInfo current, bool allowJapanese)
        => allowJapanese
        || home.DataCenterId == current.DataCenterId
        || (home.Region != WorldRegion.Japan && current.Region != WorldRegion.Japan);

    // The run's own row and panel learn how the way home went, apart from how the shopping ended.
    private void SetReturnHome(ShopSession ending, ReturnHomeResult result)
    {
        ending.ReturnHome = result;
        shopRun?.SetReturnHome(result);
        if (ending.Record is not { } record || record.ReturnHome == result)
        {
            return;
        }

        record.ReturnHome = result;
        Plugin.Instance.History.Update();
    }

    // A way home that did not run to its own end, a fault for one, says nothing of where the character stands, so the
    // after-run action is not taken after it.
    private void RunChain(bool wayHome, AfterRunAction? action)
    {
        if (wayHome)
        {
            Diag(action is { } then ? $"Run completed; taking the way home, then after-run action {then}." : "Run completed; taking the way home.");
            var ending = session;
            AutoWayHome? trip = null;
            var started = RunTask(() => trip = new AutoWayHome(shopRun), () =>
            {
                var ended = trip!.Result != ReturnHomeResult.Underway;
                var result = ended ? trip.Result : ReturnHomeResult.Failed;
                Diag($"The way home finished ({trip.Result}).");
                if (ending is not null)
                {
                    SetReturnHome(ending, result);
                }

                RunChain(wayHome: false, ended ? action : null);
            });
            if (!started)
            {
                if (ending is not null)
                {
                    SetReturnHome(ending, ReturnHomeResult.Failed);
                }

                ClearRun();
            }

            return;
        }

        if (action is { } next)
        {
            RunAfterAction(next);
            return;
        }

        ClearRun();
    }

    // The action is the run's character's alone: after a transfer home another character may stand in its place.
    private void RunAfterAction(AfterRunAction action)
    {
        var contentId = shopRun?.Authorization?.CharacterContentId ?? 0;
        if (contentId == 0 || contentId != ECommons.DalamudServices.Svc.PlayerState.ContentId)
        {
            Diag($"After-run action {action} skipped: the character logged in is not the one the run belongs to.");
            ClearRun();
            return;
        }

        Diag($"Starting after-run action {action}.");
        var started = RunTask(() => AfterRunTask(action, contentId), () =>
        {
            Diag($"After-run action {action} finished.");
            ClearRun();
        });
        if (!started)
        {
            ClearRun();
        }
    }

    private static AutoCommon AfterRunTask(AfterRunAction action, ulong contentId)
        => action == AfterRunAction.ReturnToInn ? new AutoReturnToInn() : new AutoAfterRun(action, contentId);

    // Idempotent through Recorded, so an explicit Stop and a finished task can both call it.
    private void FinalizeRun(ShopSession? ending)
    {
        if (ending is null || ending.Recorded)
        {
            return;
        }

        ending.Recorded = true;
        ending.End();
        if (ending.Interrupted)
        {
            Diag("Run was interrupted; it stays in the journal to resume and leaves no history row yet.");
            return;
        }

        try
        {
            var outcome = ending.Outcome ?? (ending.EndedWithFault ? RunOutcome.Failed : RunOutcome.Stopped);
            // A run stopped before it did anything is a misclick more often than a run; any other end is worth a row.
            if (!ending.DidWork && outcome == RunOutcome.Stopped)
            {
                Diag("Run was stopped before doing any work; nothing recorded to history.");
                return;
            }

            var record = new RunRecord
            {
                StartedAtUtc = ending.StartedAt,
                EndedAtUtc = DateTime.UtcNow,
                DurationSeconds = ending.Elapsed.TotalSeconds,
                WorldName = ending.WorldName,
                DataCenterName = ending.DataCenterName,
                CrossedDataCenter = ending.CrossedDataCenter,
                Outcome = outcome,
                ListName = ending.ListName,
                Caller = ending.Caller.Length > 0 ? ending.Caller : null,
                RunId = ending.RunId == Guid.Empty ? null : ending.RunId,
                DryRun = ending.DryRun,
                Spent = ending.Spent,
                Units = ending.Units,
                Purchases = ending.Purchases,
                ReturnHome = ending.ReturnHome,
            };
            Plugin.Instance.History.Append(record);
            ending.Record = record;
            Diag($"Run recorded to history ({outcome}{(record.DryRun ? ", dry run" : string.Empty)}): {record.Duration} on {record.WorldName} ({record.DataCenterName}), {record.Units} unit(s) for {record.Spent} gil{(record.CrossedDataCenter ? ", across data centers" : string.Empty)}.");
        }
        catch (Exception exception)
        {
            Diag($"FinalizeRun failed to record history: {exception.Message}");
        }
    }

    private void ResetFaultBudget()
    {
        faultResumeCount = 0;
        runFaultResumeCount = 0;
        faultWindowStartedAtMs = 0;
    }

    // The window restarts once it lapses, so sparse faults over a long run each get a fresh budget; only a burst, a wedge
    // that faults again straight away, spends it. The per-run cap still ends a run whose faults keep coming, however far
    // apart they are.
    private bool TryAutoResumeAfterFault(ShopSession owningSession)
    {
        if (!Plugin.Instance.Configuration.AutoResumeOnFault)
        {
            Diag("Run task faulted and auto-resume on fault is off; the run ends.");
            return false;
        }

        if (!CanRestart())
        {
            Diag("Run task faulted with nothing to resume; the run ends.");
            return false;
        }

        if (runFaultResumeCount >= MaxFaultResumesPerRun)
        {
            Diag($"Run task faulted after {runFaultResumeCount} restarts in this run; not resuming. The run ends.");
            return false;
        }

        var now = Environment.TickCount64;
        if (now - faultWindowStartedAtMs > FaultResumeWindowMs)
        {
            faultResumeCount = 0;
            faultWindowStartedAtMs = now;
        }

        if (faultResumeCount >= MaxFaultResumes)
        {
            Diag($"Run task faulted {faultResumeCount} times within {FaultResumeWindowMs / TimeUnits.MillisecondsPerMinute} minutes; not resuming. The run ends.");
            return false;
        }

        faultResumeCount++;
        runFaultResumeCount++;
        owningSession.ClearFault();
        Diag($"Run task ended on an unexpected fault; auto-resuming (resume {faultResumeCount}/{MaxFaultResumes} in this {FaultResumeWindowMs / TimeUnits.MillisecondsPerMinute} minute window, {runFaultResumeCount}/{MaxFaultResumesPerRun} in this run).");
        ECommons.DalamudServices.Svc.Chat.Print($"{AshopConstants.LogPrefix} The run stopped on an unexpected error; restarting it ({faultResumeCount}/{MaxFaultResumes}).");
        LaunchRun(owningSession);
        return true;
    }
}
