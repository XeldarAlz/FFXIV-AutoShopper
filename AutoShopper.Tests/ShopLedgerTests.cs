using System.IO;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Ledger;
using static AutoShopper.Tests.ExecutionFixtures;

namespace AutoShopper.Tests;

public sealed class ShopLedgerTests
{
    private static readonly Guid FirstAttempt = new("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid SecondAttempt = new("cccccccc-0000-0000-0000-000000000002");

    [Fact]
    public void EmptyJournalBlocksNothing()
    {
        using var directory = new TemporaryDirectory();
        var ledger = new ShopLedger(directory.Path);

        Assert.False(ledger.BlocksBuying);
        Assert.False(ledger.Review.IsDamaged);
        Assert.Null(ledger.Review.Interrupted);
        Assert.Empty(ledger.Review.Unresolved);
    }

    [Fact]
    public void SubmittedPurchaseWithoutAnswerIsUnresolvedWithItsEvidence()
    {
        using var directory = new TemporaryDirectory();
        var ledger = new ShopLedger(directory.Path);
        new JournalWriter(ledger.Journal).Authorize().Intent(FirstAttempt).Submitted(FirstAttempt);
        ledger.Invalidate();

        var unresolved = Assert.Single(ledger.Review.Unresolved);

        Assert.True(ledger.BlocksBuying);
        Assert.True(ledger.Review.NeedsReconciliation);
        Assert.False(ledger.Review.IsDamaged);
        Assert.Null(ledger.Review.Interrupted);
        Assert.Equal(FirstAttempt, unresolved.AttemptId);
        Assert.Equal(RunId, unresolved.RunId);
        Assert.True(unresolved.HasIntent);
        Assert.Equal(Ore, unresolved.ItemId);
        Assert.Equal(5, unresolved.Quantity);
        Assert.Equal(5_050, unresolved.Payable);
        Assert.Equal(60_000, unresolved.GilBefore);
        Assert.Equal(2, unresolved.OwnedBefore);
    }

    [Fact]
    public void AnsweringWentThroughCountsThePurchaseOnce()
    {
        using var directory = new TemporaryDirectory();
        var ledger = new ShopLedger(directory.Path);
        new JournalWriter(ledger.Journal).Authorize().Intent(FirstAttempt).Submitted(FirstAttempt).Unknown(FirstAttempt).Ended(RunState.NeedsReconciliation);
        ledger.Invalidate();

        var answer = ledger.Reconcile(FirstAttempt, purchased: true, Now);

        Assert.Equal(LedgerAnswer.Applied, answer);
        Assert.False(ledger.BlocksBuying);
        var run = Assert.Single(ledger.Review.Runs);
        Assert.Equal(5_050, run.ConfirmedSpend);
        Assert.Equal(0, run.ReservedSpend);
        Assert.Equal(AttemptStatus.Confirmed, Assert.Single(run.Attempts).Status);
    }

    [Fact]
    public void AnsweringDidNotGoThroughReleasesTheReservation()
    {
        using var directory = new TemporaryDirectory();
        var ledger = new ShopLedger(directory.Path);
        new JournalWriter(ledger.Journal).Authorize().Intent(FirstAttempt).Submitted(FirstAttempt);
        ledger.Invalidate();

        Assert.Equal(LedgerAnswer.Applied, ledger.Reconcile(FirstAttempt, purchased: false, Now));

        var run = Assert.Single(ledger.Review.Runs);
        Assert.False(ledger.BlocksBuying);
        Assert.Equal(0, run.ConfirmedSpend);
        Assert.Equal(0, run.ReservedSpend);
        Assert.Equal(AttemptStatus.Rejected, Assert.Single(run.Attempts).Status);
    }

    [Fact]
    public void AnsweringAnAttemptTheJournalDoesNotHoldChangesNothing()
    {
        using var directory = new TemporaryDirectory();
        var ledger = new ShopLedger(directory.Path);
        new JournalWriter(ledger.Journal).Authorize().Intent(FirstAttempt).Submitted(FirstAttempt);
        ledger.Invalidate();

        Assert.Equal(LedgerAnswer.NoSuchAttempt, ledger.Reconcile(SecondAttempt, purchased: true, Now));
        Assert.True(ledger.BlocksBuying);
    }

    [Fact]
    public void RunWithoutAnEndIsInterruptedAndDiscardEndsIt()
    {
        using var directory = new TemporaryDirectory();
        var ledger = new ShopLedger(directory.Path);
        new JournalWriter(ledger.Journal).Authorize().Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt);
        ledger.Invalidate();

        var interrupted = ledger.Review.Interrupted;
        Assert.NotNull(interrupted);
        Assert.Equal(RunId, interrupted.RunId);
        Assert.Equal(5_050, interrupted.ConfirmedSpend);
        Assert.False(ledger.BlocksBuying);

        Assert.True(ledger.Discard(Now));

        Assert.Null(ledger.Review.Interrupted);
        Assert.Equal(RunState.Stopped, Assert.Single(ledger.Review.Runs).FinalState);
    }

    [Fact]
    public void FinishedRunsMoveToTheArchive()
    {
        using var directory = new TemporaryDirectory();
        var ledger = new ShopLedger(directory.Path);
        new JournalWriter(ledger.Journal).Authorize().Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt).Ended(RunState.Completed);

        var rotation = ledger.Rotate(Now);

        Assert.True(rotation.Succeeded);
        Assert.Equal(1, rotation.ArchivedRuns);
        Assert.Empty(ledger.Review.Runs);
    }

    [Fact]
    public void OutcomeWithoutIntentIsDamageAnAnswerCannotRepair()
    {
        using var directory = new TemporaryDirectory();
        var ledger = new ShopLedger(directory.Path);
        new JournalWriter(ledger.Journal).Authorize().Confirmed(FirstAttempt);
        ledger.Invalidate();

        Assert.True(ledger.Review.IsDamaged);
        Assert.True(ledger.BlocksBuying);
        Assert.Equal(LedgerAnswer.NoSuchAttempt, ledger.Reconcile(FirstAttempt, purchased: true, Now));
    }

    [Fact]
    public void SettingADamagedJournalAsideStartsAFreshOne()
    {
        using var directory = new TemporaryDirectory();
        var ledger = new ShopLedger(directory.Path);
        new JournalWriter(ledger.Journal).Authorize().Confirmed(FirstAttempt);
        ledger.Invalidate();
        Assert.True(ledger.Review.IsDamaged);

        Assert.True(ledger.SetAside(Now, out var path));

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(ledger.Journal.FilePath));
        Assert.False(ledger.BlocksBuying);
        Assert.Empty(ledger.Review.Runs);
    }

    [Fact]
    public void DryRunJournalStaysInMemory()
    {
        using var directory = new TemporaryDirectory();
        var ledger = new ShopLedger(directory.Path);
        var memory = new MemoryJournal();
        var writer = new JournalWriter(memory).Authorize().Intent(FirstAttempt);
        writer.Append(new PurchaseSubmitted { Sequence = memory.NextSequence, RunId = RunId, AtUtc = Now, AttemptId = FirstAttempt, Sent = false });

        Assert.Equal(3, memory.Count);
        Assert.False(File.Exists(ledger.Journal.FilePath));
        Assert.False(ledger.BlocksBuying);
        Assert.Empty(ledger.Review.Runs);
        var review = JournalReview.Build(memory.Read().Entries);
        Assert.False(review.NeedsReconciliation);
        Assert.Equal(AttemptStatus.NotSent, Assert.Single(Assert.Single(review.Runs).Attempts).Status);
    }

    [Fact]
    public void MemoryJournalRefusesMalformedEvents()
    {
        var memory = new MemoryJournal();

        var appended = memory.TryAppend(new RunEnded { Sequence = 0, RunId = RunId, AtUtc = Now, FinalState = RunState.Stopped });

        Assert.False(appended);
        Assert.NotNull(memory.LastError);
        Assert.Equal(0, memory.Count);
    }
}
