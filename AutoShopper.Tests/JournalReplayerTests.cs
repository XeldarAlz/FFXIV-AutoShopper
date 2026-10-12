using System.Collections.Immutable;
using System.Text.Json;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.ExecutionFixtures;

namespace AutoShopper.Tests;

public sealed class JournalReplayerTests
{
    private static readonly Guid FirstAttempt = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid SecondAttempt = new("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid ThirdAttempt = new("aaaaaaaa-0000-0000-0000-000000000003");

    [Fact]
    public void ConfirmedPurchaseIsAppliedToProgressAndSpend()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt, 5, 5_050);

        var progress = JournalReplayer.Replay(journal.Entries);

        Assert.False(progress.NeedsReconciliation);
        Assert.Equal(5_050, progress.ConfirmedSpend);
        Assert.Equal(0, progress.ReservedSpend);
        Assert.Equal(5, OreProgress(progress).ConfirmedQuantity);
        Assert.Equal(5, OreProgress(progress).Outstanding.RequiredQuantity);
        Assert.Equal(RunState.Paused, progress.RecoveredState);
        Assert.Equal(AttemptStatus.Confirmed, Assert.Single(progress.Attempts).Status);
    }

    [Fact]
    public void DuplicateConfirmationsAreAppliedOnce()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt, 5, 5_050).Confirmed(FirstAttempt, 5, 5_050);

        var progress = JournalReplayer.Replay(journal.Entries);

        Assert.Equal(5_050, progress.ConfirmedSpend);
        Assert.Equal(5, OreProgress(progress).ConfirmedQuantity);
        Assert.False(progress.NeedsReconciliation);
    }

    [Fact]
    public void RepeatedJournalLinesAreDeduplicated()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt, 5, 5_050);
        var entries = new List<JournalEvent?>(journal.Entries);
        entries.Add(entries[^1]);
        entries.Add(entries[1]);

        var progress = JournalReplayer.Replay(entries);

        Assert.Equal(5_050, progress.ConfirmedSpend);
        Assert.Equal(1, progress.AttemptCount);
        Assert.Equal(2, Count(progress, JournalAnomalyKind.DuplicateEvent));
        Assert.False(progress.NeedsReconciliation);
    }

    [Fact]
    public void OutOfOrderEventsStillApplyEachConfirmationOnce()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt, 5, 5_050);
        journal.Intent(SecondAttempt, listingId: 78).Submitted(SecondAttempt).Confirmed(SecondAttempt, 5, 5_050);
        var shuffled = new List<JournalEvent?>(journal.Entries);
        shuffled.Reverse();

        var progress = JournalReplayer.Replay(shuffled);

        Assert.Equal(10_100, progress.ConfirmedSpend);
        Assert.Equal(10, OreProgress(progress).ConfirmedQuantity);
        Assert.Equal([FirstAttempt, SecondAttempt], AttemptIds(progress));
        Assert.False(progress.NeedsReconciliation);
    }

    [Fact]
    public void TornTailIsIgnoredAndReported()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt, 5, 5_050);
        var entries = new List<JournalEvent?>(journal.Entries) { null };

        var progress = JournalReplayer.Replay(entries);

        Assert.True(progress.TornTail);
        Assert.Equal(1, Count(progress, JournalAnomalyKind.TornTail));
        Assert.False(progress.NeedsReconciliation);
        Assert.Equal(5_050, progress.ConfirmedSpend);
    }

    [Fact]
    public void InvalidTrailingEventIsTreatedAsATornTail()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt);
        var entries = new List<JournalEvent?>(journal.Entries)
        {
            new PurchaseConfirmed { Sequence = 0, RunId = RunId, AtUtc = Now, AttemptId = FirstAttempt, Quantity = 5, Payable = 5_050 },
        };

        var progress = JournalReplayer.Replay(entries);

        Assert.True(progress.TornTail);
        Assert.Equal(AttemptStatus.Unknown, Assert.Single(progress.Attempts).Status);
        Assert.True(progress.NeedsReconciliation);
    }

    [Fact]
    public void CorruptEntryInsideTheJournalBlocksBuying()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt, 5, 5_050);
        var entries = new List<JournalEvent?>(journal.Entries);
        entries.Insert(2, null);

        var progress = JournalReplayer.Replay(entries);

        Assert.Equal(1, Count(progress, JournalAnomalyKind.CorruptEntry));
        Assert.False(progress.TornTail);
        Assert.True(progress.NeedsReconciliation);
        Assert.Equal(RunState.NeedsReconciliation, progress.RecoveredState);
    }

    [Fact]
    public void CrashBetweenIntentAndResultLeavesAnUnknownReservation()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt);

        var progress = JournalReplayer.Replay(journal.Entries);

        var attempt = Assert.Single(progress.Attempts);
        Assert.Equal(AttemptStatus.Unknown, attempt.Status);
        Assert.Equal(5_050, progress.ReservedSpend);
        Assert.Equal(5, OreProgress(progress).UnknownQuantity);
        Assert.Equal(0, OreProgress(progress).ConfirmedQuantity);
        Assert.True(progress.NeedsReconciliation);
        Assert.Equal(RunState.NeedsReconciliation, progress.RecoveredState);

        var book = progress.ToSpendBook();
        Assert.Equal(FirstAttempt, book.PendingAttemptId);
        Assert.Equal(5_050, book.Reserved);
        Assert.Equal(MaxSpend - 5_050, book.Remaining);
    }

    [Fact]
    public void SubmittedPurchaseWithoutAnOutcomeIsUnknown()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt);

        var progress = JournalReplayer.Replay(journal.Entries);

        Assert.Equal(AttemptStatus.Unknown, Assert.Single(progress.Attempts).Status);
        Assert.True(progress.NeedsReconciliation);
    }

    [Fact]
    public void PurchaseThatWasNeverSentReleasesTheReservation()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt, sent: false);

        var progress = JournalReplayer.Replay(journal.Entries);

        Assert.Equal(AttemptStatus.NotSent, Assert.Single(progress.Attempts).Status);
        Assert.Equal(0, progress.ReservedSpend);
        Assert.Equal(1, progress.FailedAttemptCount);
        Assert.False(progress.NeedsReconciliation);
    }

    [Fact]
    public void RejectedPurchaseCountsAsAFailedAttempt()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Rejected(FirstAttempt);

        var progress = JournalReplayer.Replay(journal.Entries);

        Assert.Equal(AttemptStatus.Rejected, Assert.Single(progress.Attempts).Status);
        Assert.Equal(1, OreProgress(progress).FailedAttempts);
        Assert.Equal(1, progress.FailedAttemptCount);
        Assert.Equal(0, progress.ConfirmedSpend);
        Assert.False(progress.NeedsReconciliation);
    }

    [Fact]
    public void ReconciliationResolvesAnUnknownExactlyOnce()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Unknown(FirstAttempt)
            .Reconciled(FirstAttempt, true, 5, 5_050)
            .Reconciled(FirstAttempt, true, 5, 5_050);

        var progress = JournalReplayer.Replay(journal.Entries);
        var again = JournalReplayer.Replay(journal.Entries);

        Assert.Equal(AttemptStatus.Confirmed, Assert.Single(progress.Attempts).Status);
        Assert.Equal(5_050, progress.ConfirmedSpend);
        Assert.Equal(5, OreProgress(progress).ConfirmedQuantity);
        Assert.False(progress.NeedsReconciliation);
        Assert.Equal(progress.ConfirmedSpend, again.ConfirmedSpend);
    }

    [Fact]
    public void ReconciledAsNotPurchasedReleasesTheReservation()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Unknown(FirstAttempt).Reconciled(FirstAttempt, false, 0, 0);

        var progress = JournalReplayer.Replay(journal.Entries);

        Assert.Equal(AttemptStatus.Rejected, Assert.Single(progress.Attempts).Status);
        Assert.Equal(0, progress.ReservedSpend);
        Assert.False(progress.NeedsReconciliation);
    }

    [Fact]
    public void LateConfirmationAfterAnUnknownIsApplied()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Unknown(FirstAttempt).Confirmed(FirstAttempt, 5, 5_050);

        var progress = JournalReplayer.Replay(journal.Entries);

        Assert.Equal(AttemptStatus.Confirmed, Assert.Single(progress.Attempts).Status);
        Assert.False(progress.NeedsReconciliation);
    }

    [Fact]
    public void ContradictoryOutcomesNeedReconciliation()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt, 5, 5_050).Rejected(FirstAttempt);
        journal.Intent(SecondAttempt, listingId: 78).Submitted(SecondAttempt).Confirmed(SecondAttempt, 4, 4_040);

        var progress = JournalReplayer.Replay(journal.Entries);

        Assert.All(progress.Attempts, attempt => Assert.Equal(AttemptStatus.Unknown, attempt.Status));
        Assert.Equal(2, Count(progress, JournalAnomalyKind.ConflictingOutcome));
        Assert.Equal(0, progress.ConfirmedSpend);
        Assert.Equal(10_100, progress.ReservedSpend);
        Assert.True(progress.NeedsReconciliation);
    }

    [Fact]
    public void RetryCountsAreNeverResetByReplay()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Checkpoint(attemptCount: 7, failedAttemptCount: 5);
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Rejected(FirstAttempt);
        journal.Checkpoint(attemptCount: 1, failedAttemptCount: 1);

        var progress = JournalReplayer.Replay(journal.Entries);

        Assert.Equal(7, progress.AttemptCount);
        Assert.Equal(5, progress.FailedAttemptCount);
    }

    [Fact]
    public void ExclusionsAndCompletedWorldsOnlyGrow()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Checkpoint(excluded: ["game:1", "game:2"], completed: [HomeWorld]);
        journal.Checkpoint(excluded: ["game:3"], completed: []);

        var progress = JournalReplayer.Replay(journal.Entries);

        Assert.Equal(["game:1", "game:2", "game:3"], progress.ExcludedListingKeys.AsSpan().ToArray());
        Assert.Equal([HomeWorld], progress.CompletedWorldIds.AsSpan().ToArray());
        Assert.Equal(RunState.Searching, progress.LastCheckpointState);
    }

    [Fact]
    public void OutcomeWithoutAnIntentBlocksBuying()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Confirmed(ThirdAttempt, 5, 5_050);

        var progress = JournalReplayer.Replay(journal.Entries);

        Assert.Equal(1, Count(progress, JournalAnomalyKind.OrphanOutcome));
        Assert.True(progress.NeedsReconciliation);
        Assert.Equal(0, progress.ConfirmedSpend);
    }

    [Fact]
    public void IntentForADemandOutsideTheAuthorizationBlocksBuying()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt, demand: new DemandKey(Ore, DemandQuality.HighQuality)).Submitted(FirstAttempt).Confirmed(FirstAttempt, 5, 5_050);

        var progress = JournalReplayer.Replay(journal.Entries);

        Assert.Equal(1, Count(progress, JournalAnomalyKind.UnauthorizedDemand));
        Assert.True(progress.NeedsReconciliation);
    }

    [Fact]
    public void LatestAuthorizedRunIsReplayed()
    {
        var olderRun = new Guid("bbbbbbbb-0000-0000-0000-000000000001");
        var journal = new JournalBuilder();
        journal.Authorize(olderRun).Intent(FirstAttempt, runId: olderRun);
        journal.Authorize().Intent(SecondAttempt).Submitted(SecondAttempt).Confirmed(SecondAttempt, 5, 5_050);

        var latest = JournalReplayer.Replay(journal.Entries);
        var older = JournalReplayer.Replay(journal.Entries, olderRun);

        Assert.Equal(RunId, latest.RunId);
        Assert.Equal([SecondAttempt], AttemptIds(latest));
        Assert.False(latest.NeedsReconciliation);
        Assert.Equal(olderRun, older.RunId);
        Assert.True(older.NeedsReconciliation);
    }

    [Fact]
    public void EventsOfAnUnauthorizedRunBlockBuying()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt, runId: Guid.NewGuid());

        var progress = JournalReplayer.Replay(journal.Entries);

        Assert.Equal(1, Count(progress, JournalAnomalyKind.UnauthorizedRun));
        Assert.True(progress.NeedsReconciliation);
    }

    [Fact]
    public void EmptyJournalIsIdle()
    {
        var progress = JournalReplayer.Replay([]);

        Assert.Null(progress.Authorization);
        Assert.False(progress.NeedsReconciliation);
        Assert.Equal(RunState.Idle, progress.RecoveredState);
    }

    [Fact]
    public void EndedRunRecoversItsFinalState()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt, 5, 5_050).Ended(RunState.CompletedPartial);

        var progress = JournalReplayer.Replay(journal.Entries);

        Assert.Equal(RunState.CompletedPartial, progress.FinalState);
        Assert.Equal(RunState.CompletedPartial, progress.RecoveredState);
    }

    [Fact]
    public void ConfirmedPurchasesUseGameListingKeys()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt, 5, 5_050);

        var purchase = Assert.Single(JournalReplayer.Replay(journal.Entries).ConfirmedPurchases());

        Assert.Equal(new ConfirmedPurchase(OreDemand.Key, ListingKeys.ForGameListing(77), 5, 5_050), purchase);
    }

    [Fact]
    public void JsonRoundTripKeepsTheShapeAndTheReplay()
    {
        var journal = new JournalBuilder().Authorize();
        journal.Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt, 5, 5_050);
        journal.Intent(SecondAttempt, listingId: 78).Submitted(SecondAttempt).Unknown(SecondAttempt).Reconciled(SecondAttempt, false, 0, 0);
        journal.Intent(ThirdAttempt, listingId: 79).Submitted(ThirdAttempt).Rejected(ThirdAttempt);
        journal.Checkpoint(excluded: ["game:79"], completed: [HomeWorld]).Ended(RunState.Stopped);

        var lines = new List<string>();
        var restored = new List<JournalEvent?>();
        foreach (var entry in journal.Entries)
        {
            var line = JsonSerializer.Serialize(entry);
            lines.Add(line);
            restored.Add(JsonSerializer.Deserialize<JournalEvent>(line));
        }

        Assert.StartsWith("{\"Kind\":\"RunAuthorized\"", lines[0], StringComparison.Ordinal);
        Assert.Contains("\"Quality\":\"Any\"", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"FinalState\":\"Stopped\"", lines[^1], StringComparison.Ordinal);
        var original = JournalReplayer.Replay(journal.Entries);
        var replayed = JournalReplayer.Replay(restored);
        Assert.Equal(original.ConfirmedSpend, replayed.ConfirmedSpend);
        Assert.Equal(original.FailedAttemptCount, replayed.FailedAttemptCount);
        Assert.Equal(original.ExcludedListingKeys.AsSpan().ToArray(), replayed.ExcludedListingKeys.AsSpan().ToArray());
        Assert.Equal(original.Attempts.AsSpan().ToArray(), replayed.Attempts.AsSpan().ToArray());
        Assert.Equal(original.Authorization!.Demands.AsSpan().ToArray(), replayed.Authorization!.Demands.AsSpan().ToArray());
        Assert.Equal(RunState.Stopped, replayed.RecoveredState);
        Assert.Empty(replayed.Anomalies);
    }

    private static DemandProgress OreProgress(JournalProgress progress)
    {
        for (var index = 0; index < progress.Demands.Length; index++)
        {
            if (progress.Demands[index].Demand.Key == OreDemand.Key)
            {
                return progress.Demands[index];
            }
        }

        throw new Xunit.Sdk.XunitException("no ore progress");
    }

    private static int Count(JournalProgress progress, JournalAnomalyKind kind)
    {
        var count = 0;
        for (var index = 0; index < progress.Anomalies.Length; index++)
        {
            if (progress.Anomalies[index].Kind == kind)
            {
                count++;
            }
        }

        return count;
    }

    private static Guid[] AttemptIds(JournalProgress progress)
    {
        var ids = new Guid[progress.Attempts.Length];
        for (var index = 0; index < ids.Length; index++)
        {
            ids[index] = progress.Attempts[index].AttemptId;
        }

        return ids;
    }

    private sealed class JournalBuilder
    {
        private readonly List<JournalEvent?> entries = new();
        private long sequence;

        public IReadOnlyList<JournalEvent?> Entries => entries;

        public JournalBuilder Authorize(Guid? runId = null)
        {
            var authorization = Authorization();
            var recorded = authorization.ToJournalEvent(NextSequence(), Now);
            entries.Add(recorded with { RunId = runId ?? RunId });
            return this;
        }

        public JournalBuilder Intent(Guid attemptId, ulong listingId = 77, DemandKey? demand = null, Guid? runId = null)
        {
            var key = demand ?? OreDemand.Key;
            entries.Add(new IntentRecorded
            {
                Sequence = NextSequence(),
                RunId = runId ?? RunId,
                AtUtc = Now,
                AttemptId = attemptId,
                ItemId = key.ItemId,
                Quality = key.Quality,
                ListingId = listingId,
                RetainerId = 501,
                WorldId = HomeWorld,
                IsHq = key.Quality == DemandQuality.HighQuality,
                Quantity = 5,
                UnitPrice = 1_000,
                TotalTax = 50,
                Payable = 5_050,
                SearchGeneration = SearchGeneration,
                PlanVersion = 1,
            });
            return this;
        }

        public JournalBuilder Submitted(Guid attemptId, bool sent = true)
        {
            entries.Add(new PurchaseSubmitted { Sequence = NextSequence(), RunId = RunId, AtUtc = Now, AttemptId = attemptId, Sent = sent });
            return this;
        }

        public JournalBuilder Confirmed(Guid attemptId, int quantity, long payable)
        {
            entries.Add(new PurchaseConfirmed { Sequence = NextSequence(), RunId = RunId, AtUtc = Now, AttemptId = attemptId, Quantity = quantity, Payable = payable });
            return this;
        }

        public JournalBuilder Rejected(Guid attemptId)
        {
            entries.Add(new PurchaseRejected { Sequence = NextSequence(), RunId = RunId, AtUtc = Now, AttemptId = attemptId, ReasonCode = 4523 });
            return this;
        }

        public JournalBuilder Unknown(Guid attemptId)
        {
            entries.Add(new PurchaseUnknown { Sequence = NextSequence(), RunId = RunId, AtUtc = Now, AttemptId = attemptId, Reason = "timeout" });
            return this;
        }

        public JournalBuilder Reconciled(Guid attemptId, bool purchased, int quantity, long payable)
        {
            entries.Add(new Reconciled { Sequence = NextSequence(), RunId = RunId, AtUtc = Now, AttemptId = attemptId, Purchased = purchased, Quantity = quantity, Payable = payable });
            return this;
        }

        public JournalBuilder Checkpoint(int attemptCount = 0, int failedAttemptCount = 0, ImmutableArray<string>? excluded = null, ImmutableArray<uint>? completed = null)
        {
            entries.Add(new Checkpoint
            {
                Sequence = NextSequence(),
                RunId = RunId,
                AtUtc = Now,
                State = RunState.Searching,
                PlanVersion = 1,
                AttemptCount = attemptCount,
                FailedAttemptCount = failedAttemptCount,
                ExcludedListingKeys = excluded ?? [],
                CompletedWorldIds = completed ?? [],
            });
            return this;
        }

        public JournalBuilder Ended(RunState finalState)
        {
            entries.Add(new RunEnded { Sequence = NextSequence(), RunId = RunId, AtUtc = Now, FinalState = finalState });
            return this;
        }

        private long NextSequence()
        {
            sequence++;
            return sequence;
        }
    }
}
