using System.Collections.Immutable;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Ledger;
using AutoShopper.Core.Market;
using AutoShopper.Core.Market.Model;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Stats;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Tasks;

internal readonly record struct InFlightPurchase(PurchaseAttempt Attempt, DemandKey Demand, string ListingKey, int Quantity, long Payable);

internal enum SettledKind : byte
{
    None = 0,
    Confirmed = 1,
    Rejected = 2,
    Unknown = 3,
}

// Durable is false when the outcome could not be written, and a run must not buy on after an outcome it cannot prove.
internal readonly record struct Settlement(SettledKind Kind, Guid AttemptId, PurchaseRejection Rejection, bool Durable);

// One run of the buyer, kept across pauses, resumes and fault restarts, since each of them builds the run task afresh.
// A purchase that left is settled here on the framework tick rather than in the task, so a Pause, a Stop or a fault
// that unwinds the task still sees its answer through: the journal, the spend book and the tally learn it either way.
internal sealed class ShopRunState : IDisposable
{
    private readonly MarketServices market;
    private readonly HashSet<string> excluded = new(StringComparer.Ordinal);
    private readonly List<uint> completedWorlds = [];
    private readonly List<RunIssue> issues = [];
    private readonly IFramework.OnUpdateDelegate settleTick;
    private ImmutableArray<string> excludedSnapshot = ImmutableArray<string>.Empty;
    private bool settleHooked;
    private bool disposed;

    private ShopRunState(MarketServices market, IRunJournal journal, Guid runId, bool dryRun)
    {
        this.market = market;
        Journal = journal;
        RunId = runId;
        DryRun = dryRun;
        settleTick = _ => SettleIfAnswered();
        Spend = new SpendBook(0);
        Tally = new RunTally(ImmutableArray<PlanDemand>.Empty);
    }

    public Guid RunId { get; }

    public bool DryRun { get; }

    public IRunJournal Journal { get; }

    public string ListName { get; private set; } = string.Empty;

    public RunAuthorization? Authorization { get; private set; }

    public SpendBook Spend { get; private set; }

    public RunTally Tally { get; private set; }

    public PurchasePlan? Plan { get; private set; }

    // The stops behind the run and ahead of it; null until the run has a plan.
    public RunRoute? Route { get; private set; }

    public TravelExclusions Exclusions { get; } = new();

    // The worlds the run planned on when it was authorized or resumed; a replan never reaches past them.
    public ImmutableArray<PlanWorld> RunWorlds { get; private set; } = ImmutableArray<PlanWorld>.Empty;

    // The estimates the run plans what is left on, refreshed only once older than the stale setting.
    public PriceSnapshot? Prices { get; private set; }

    public long LastPriceRefreshAtMs { get; set; }

    public TravelPhase TravelPhase { get; private set; }

    public string TravelTarget { get; private set; } = string.Empty;

    public long TravelStartedAtMs { get; private set; }

    // Set while a data center transfer this run asked for is under way, from right before the request to the check
    // after the login it brings.
    public bool TransferArmed { get; private set; }

    public bool TransferLoggedOut { get; private set; }

    public TransferSnapshot? PendingAtLogout { get; private set; }

    public ReturnHomeResult ReturnHome { get; private set; }

    // Stops on a world the run had already shopped.
    public int Revisits { get; private set; }

    public IReadOnlyList<uint> CompletedWorldIds => completedWorlds;

    public RunState State { get; private set; } = RunState.Preflight;

    public InFlightPurchase? InFlight { get; private set; }

    public Settlement LastSettlement { get; private set; }

    public int AttemptCount { get; private set; }

    public int FailedAttemptCount { get; private set; }

    public RunState? FinalState { get; private set; }

    public long LastGil { get; private set; } = MarketGil.Unavailable;

    // Rises with every change the windows show, so text built from the run is rebuilt only then.
    public int Revision { get; private set; }

    public ImmutableArray<string> ExcludedListingKeys => excludedSnapshot;

    public IReadOnlyList<RunIssue> Issues => issues;

    public bool IsAuthorized => Authorization is not null;

    public bool HasUnknownOutcome => LastSettlement.Kind == SettledKind.Unknown || (LastSettlement.Kind != SettledKind.None && !LastSettlement.Durable);

    public Action? Settled { get; set; }

    public static RunOutcome OutcomeOf(RunState state) => state switch
    {
        RunState.Completed => RunOutcome.Completed,
        RunState.CompletedPartial => RunOutcome.CompletedPartial,
        RunState.NeedsReconciliation => RunOutcome.NeedsReconciliation,
        RunState.Failed => RunOutcome.Failed,
        _ => RunOutcome.Stopped,
    };

    public static ShopRunState Fresh(MarketServices market, IRunJournal journal, bool dryRun, string listName)
        => new(market, journal, Guid.NewGuid(), dryRun) { ListName = listName };

    // A run the journal says was interrupted: its authorization, its confirmed purchases and its exclusions come back;
    // its plan is made again from fresh prices.
    public static ShopRunState FromJournal(MarketServices market, IRunJournal journal, JournalProgress progress)
    {
        var authorization = progress.Authorization!;
        var state = new ShopRunState(market, journal, progress.RunId, dryRun: false)
        {
            Authorization = authorization,
            Spend = progress.ToSpendBook(),
            Tally = RunTally.Restore(authorization.Demands, progress.ConfirmedPurchases()),
            AttemptCount = progress.AttemptCount,
            FailedAttemptCount = progress.FailedAttemptCount,
        };
        for (var index = 0; index < progress.ExcludedListingKeys.Length; index++)
        {
            state.excluded.Add(progress.ExcludedListingKeys[index]);
        }

        for (var index = 0; index < progress.CompletedWorldIds.Length; index++)
        {
            state.completedWorlds.Add(progress.CompletedWorldIds[index]);
        }

        state.Exclusions.Restore(progress.ExcludedWorldIds);
        state.RefreshExcludedSnapshot();
        return state;
    }

    public bool Authorize(RunAuthorization authorization, PurchasePlan plan, PriceSnapshot? prices, DateTime nowUtc)
    {
        if (!Journal.TryAppend(authorization.ToJournalEvent(Journal.NextSequence, nowUtc)))
        {
            return false;
        }

        Authorization = authorization;
        StartRoute(plan, plan.Request.Worlds, prices, plan.Request.Settings.MaxStops);
        Spend = new SpendBook(authorization.MaxSpend);
        Tally = new RunTally(authorization.Demands);
        Changed();
        return true;
    }

    // The run's most stops setting as it stood when the run planned first, 0 for no limit.
    public int MaxStops { get; private set; }

    public void StartRoute(PurchasePlan plan, ImmutableArray<PlanWorld> runWorlds, PriceSnapshot? prices, int maxStops)
    {
        Plan = plan;
        MaxStops = Math.Max(0, maxStops);
        RunWorlds = runWorlds.IsDefault ? ImmutableArray<PlanWorld>.Empty : runWorlds;
        Prices = prices;
        Route = RunRoute.Start(plan.Stops);
        Changed();
    }

    public void Replanned(PurchasePlan plan)
    {
        Plan = plan;
        Route?.ReplaceAhead(plan.Stops);
        Changed();
    }

    public void UsePrices(PriceSnapshot prices)
    {
        Prices = prices;
        Changed();
    }

    public void BeginStop(int index)
    {
        Route?.Begin(index);
        Changed();
    }

    public void CompleteStop(int index)
    {
        if (Route is not { } route || (uint)index >= (uint)route.Count)
        {
            return;
        }

        var worldId = route[index].World.WorldId;
        if (completedWorlds.Contains(worldId))
        {
            Revisits++;
        }

        route.Complete(index);
        MarkWorldCompleted(worldId);
        Changed();
    }

    public void SkipStop(int index, RouteSkip skip, TravelFailure failure)
    {
        Route?.Skip(index, skip, failure);
        Changed();
    }

    public void SkipAhead(RouteSkip skip, TravelFailure failure)
    {
        Route?.SkipAhead(skip, failure);
        Changed();
    }

    public ExclusionStep RecordTravelFailure(in PlanWorld target, TravelFailure failure, uint currentDataCenterId)
    {
        var step = Exclusions.Record(target, failure, currentDataCenterId);
        Changed();
        return step;
    }

    public void RecordArrival()
    {
        Exclusions.RecordArrival();
        Changed();
    }

    public void SetTravel(TravelPhase phase, string target)
    {
        if (TravelPhase == phase && string.Equals(TravelTarget, target, StringComparison.Ordinal))
        {
            return;
        }

        TravelPhase = phase;
        TravelTarget = target;
        TravelStartedAtMs = Environment.TickCount64;
        Changed();
    }

    public void ClearTravel() => SetTravel(TravelPhase.None, string.Empty);

    public void ArmTransfer()
    {
        TransferArmed = true;
        TransferLoggedOut = false;
        PendingAtLogout = null;
        Changed();
    }

    public void NoteTransferLogout(TransferSnapshot? pending)
    {
        TransferLoggedOut = true;
        PendingAtLogout = pending;
        Changed();
    }

    public void EndTransfer()
    {
        if (!TransferArmed && !TransferLoggedOut && PendingAtLogout is null)
        {
            return;
        }

        TransferArmed = false;
        TransferLoggedOut = false;
        PendingAtLogout = null;
        Changed();
    }

    public void SetReturnHome(ReturnHomeResult result)
    {
        if (ReturnHome == result)
        {
            return;
        }

        ReturnHome = result;
        Changed();
    }

    public void SetState(RunState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        Changed();
    }

    public void NoteGil(long gil)
    {
        if (gil == LastGil)
        {
            return;
        }

        LastGil = gil;
        Changed();
    }

    public void CountAttempt()
    {
        AttemptCount++;
        Changed();
    }

    public void CountFailedAttempt()
    {
        FailedAttemptCount++;
        Changed();
    }

    public void Exclude(string listingKey)
    {
        if (string.IsNullOrEmpty(listingKey) || !excluded.Add(listingKey))
        {
            return;
        }

        RefreshExcludedSnapshot();
        Changed();
    }

    public bool IsWorldCompleted(uint worldId) => completedWorlds.Contains(worldId);

    public bool HasIssueFor(uint itemId)
    {
        for (var index = 0; index < issues.Count; index++)
        {
            if (issues[index].ItemId == itemId)
            {
                return true;
            }
        }

        return false;
    }

    // What the history row shows: a dry run's "would buy" totals, a real run's confirmed ones.
    public void ApplyTo(ShopSession session)
    {
        session.DryRun = DryRun;
        session.ListName = ListName;
        session.Spent = DryRun ? Tally.WouldBuySpend : Tally.ConfirmedSpend;
        session.Units = DryRun ? Tally.WouldBuyUnits : Tally.ConfirmedUnits;
        session.Purchases = Tally.Purchases;
    }

    public void MarkWorldCompleted(uint worldId)
    {
        if (completedWorlds.Contains(worldId))
        {
            return;
        }

        completedWorlds.Add(worldId);
        Changed();
    }

    // The same reason for the same item on the same world is one issue, carrying the newest count.
    public void AddIssue(RunIssueReason reason, uint itemId, uint worldId, int quantity)
    {
        var issue = new RunIssue(reason, itemId, worldId, Math.Max(0, quantity), DateTime.UtcNow);
        for (var index = 0; index < issues.Count; index++)
        {
            var existing = issues[index];
            if (existing.Reason == reason && existing.ItemId == itemId && existing.WorldId == worldId)
            {
                issues[index] = issue;
                Changed();
                return;
            }
        }

        issues.Add(issue);
        RunLog.Info($"Run: issue {reason} for item {itemId} on world {worldId}, {quantity} unit(s).");
        Changed();
    }

    public void BeginInFlight(in InFlightPurchase purchase)
    {
        InFlight = purchase;
        LastSettlement = default;
        if (!settleHooked && !disposed)
        {
            Svc.Framework.Update += settleTick;
            settleHooked = true;
        }

        Changed();
    }

    public bool Checkpoint(DateTime nowUtc)
    {
        if (Authorization is null)
        {
            return true;
        }

        var written = Journal.TryAppend(new Checkpoint
        {
            Sequence = Journal.NextSequence,
            RunId = RunId,
            AtUtc = nowUtc,
            State = State,
            PlanVersion = Plan?.Version ?? 0,
            CompletedWorldIds = [.. completedWorlds],
            ExcludedListingKeys = excludedSnapshot,
            ExcludedWorldIds = Exclusions.ExcludedWorldIds,
            AttemptCount = AttemptCount,
            FailedAttemptCount = FailedAttemptCount,
        });
        if (!written)
        {
            RunLog.Warning($"Run: the checkpoint could not be written: {Journal.LastError}.");
        }

        return written;
    }

    // Writes the run's end once. A run that never got its authorization has nothing to end.
    public bool Finish(RunState finalState, string reason, DateTime nowUtc)
    {
        if (FinalState is not null)
        {
            return true;
        }

        FinalState = finalState;
        State = finalState;
        Changed();
        if (Authorization is null)
        {
            return true;
        }

        var written = Journal.TryAppend(new RunEnded
        {
            Sequence = Journal.NextSequence,
            RunId = RunId,
            AtUtc = nowUtc,
            FinalState = finalState,
            Reason = reason,
        });
        if (!written)
        {
            RunLog.Warning($"Run: the end of the run could not be written: {Journal.LastError}.");
        }

        return written;
    }

    public void Dispose()
    {
        disposed = true;
        Unhook();
    }

    private void SettleIfAnswered()
    {
        if (InFlight is not { } purchase)
        {
            Unhook();
            return;
        }

        var outcome = market.Watcher.Poll();
        if (outcome.Kind is PurchaseOutcomeKind.Pending)
        {
            return;
        }

        var attemptId = purchase.Attempt.AttemptId;
        var kind = outcome.AttemptId == attemptId ? outcome.Kind : PurchaseOutcomeKind.Unknown;
        if (outcome.Kind == PurchaseOutcomeKind.None)
        {
            RunLog.Warning($"Run: purchase {attemptId} has no answer to wait for; it counts as uncertain.");
        }

        var durable = kind switch
        {
            PurchaseOutcomeKind.Confirmed => SettleConfirmed(purchase, outcome),
            PurchaseOutcomeKind.Rejected => SettleRejected(purchase, outcome),
            _ => SettleUnknown(purchase, outcome),
        };
        market.Watcher.Acknowledge(attemptId);
        NoteGil(outcome.GilAfter);
        var settled = kind switch
        {
            PurchaseOutcomeKind.Confirmed => SettledKind.Confirmed,
            PurchaseOutcomeKind.Rejected => SettledKind.Rejected,
            _ => SettledKind.Unknown,
        };
        LastSettlement = new Settlement(settled, attemptId, outcome.Rejection, durable);
        InFlight = null;
        Checkpoint(DateTime.UtcNow);
        Unhook();
        Changed();
        Settled?.Invoke();
    }

    private bool SettleConfirmed(in InFlightPurchase purchase, in PurchaseOutcome outcome)
    {
        var written = Journal.TryAppend(new PurchaseConfirmed
        {
            Sequence = Journal.NextSequence,
            RunId = RunId,
            AtUtc = DateTime.UtcNow,
            AttemptId = purchase.Attempt.AttemptId,
            Quantity = purchase.Quantity,
            Payable = purchase.Payable,
            Evidence = Describe(outcome),
        });
        Spend.Confirm(purchase.Attempt.AttemptId, purchase.Payable);
        Tally.RecordConfirmed(purchase.Demand, purchase.ListingKey, purchase.Quantity, purchase.Payable);
        RunLog.Info($"Run: purchase {purchase.Attempt.AttemptId} confirmed, {purchase.Quantity} unit(s) for {purchase.Payable} gil{(written ? string.Empty : $"; the journal could not record it: {Journal.LastError}")}.");
        return written;
    }

    private bool SettleRejected(in InFlightPurchase purchase, in PurchaseOutcome outcome)
    {
        var written = Journal.TryAppend(new PurchaseRejected
        {
            Sequence = Journal.NextSequence,
            RunId = RunId,
            AtUtc = DateTime.UtcNow,
            AttemptId = purchase.Attempt.AttemptId,
            ReasonCode = (int)outcome.Rejection,
            Reason = $"{outcome.Rejection}; {Describe(outcome)}",
        });
        Spend.Release(purchase.Attempt.AttemptId);
        FailedAttemptCount++;
        RunLog.Info($"Run: purchase {purchase.Attempt.AttemptId} refused ({outcome.Rejection}){(written ? string.Empty : $"; the journal could not record it: {Journal.LastError}")}.");
        return written;
    }

    // The reservation stays: an uncertain purchase may have been charged, so its gil is spoken for until the player
    // says whether it went through.
    private bool SettleUnknown(in InFlightPurchase purchase, in PurchaseOutcome outcome)
    {
        var written = Journal.TryAppend(new PurchaseUnknown
        {
            Sequence = Journal.NextSequence,
            RunId = RunId,
            AtUtc = DateTime.UtcNow,
            AttemptId = purchase.Attempt.AttemptId,
            Reason = $"{outcome.UnknownCause}; {Describe(outcome)}",
        });
        AddIssue(RunIssueReason.UncertainPurchase, MarketItemId.Normalize(purchase.Attempt.Listing.ItemId), purchase.Attempt.Listing.WorldId, purchase.Quantity);
        RunLog.Warning($"Run: purchase {purchase.Attempt.AttemptId} has no clear answer ({outcome.UnknownCause}); buying stops until it is reconciled.");
        return written;
    }

    private static string Describe(in PurchaseOutcome outcome)
        => $"{outcome.Evidence}; response code {outcome.RawErrorId}, log message {outcome.RawLogMessageId}; gil {outcome.GilBefore} to {outcome.GilAfter}";

    private void RefreshExcludedSnapshot()
    {
        var keys = new string[excluded.Count];
        excluded.CopyTo(keys);
        Array.Sort(keys, StringComparer.Ordinal);
        excludedSnapshot = [.. keys];
    }

    private void Unhook()
    {
        if (!settleHooked)
        {
            return;
        }

        Svc.Framework.Update -= settleTick;
        settleHooked = false;
    }

    private void Changed() => Revision++;
}
