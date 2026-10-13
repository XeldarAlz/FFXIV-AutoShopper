using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Core.Ledger;

internal enum LedgerAnswer : byte
{
    Applied = 0,
    NoSuchAttempt = 1,
    WriteFailed = 2,
}

// The journal file of real runs and what it says about them: attempts nobody knows the outcome of, damage, and a run
// that was interrupted. The review is read again only after something wrote to the journal. Framework thread only.
internal sealed class ShopLedger
{
    private const string AnsweredPurchased = "the player said it went through";
    private const string AnsweredNotPurchased = "the player said it did not go through";
    private const string DiscardReason = "discarded by the player after an interruption";

    private JournalReview? review;

    public ShopLedger(string directoryPath)
    {
        Journal = new RunJournal(directoryPath);
    }

    public RunJournal Journal { get; }

    // Rises whenever the review is read again, so text built from it is rebuilt only then.
    public int Revision { get; private set; }

    public string? ReadError { get; private set; }

    public JournalReview Review => review ??= Load();

    public bool BlocksBuying => ReadError is not null || Review.NeedsReconciliation;

    public void Invalidate() => review = null;

    // Finished runs with nothing left to reconcile move to a dated archive, so the journal stays short.
    public JournalRotation Rotate(DateTime nowUtc)
    {
        var rotation = Journal.Rotate(nowUtc);
        Invalidate();
        return rotation;
    }

    public LedgerAnswer Reconcile(Guid attemptId, bool purchased, DateTime nowUtc)
    {
        var current = Review;
        for (var index = 0; index < current.Unresolved.Length; index++)
        {
            var attempt = current.Unresolved[index];
            if (attempt.AttemptId != attemptId || !attempt.HasIntent)
            {
                continue;
            }

            var answer = new Reconciled
            {
                Sequence = Journal.NextSequence,
                RunId = attempt.RunId,
                AtUtc = nowUtc,
                AttemptId = attempt.AttemptId,
                Purchased = purchased,
                Quantity = purchased ? attempt.Quantity : 0,
                Payable = purchased ? attempt.Payable : 0,
                Note = purchased ? AnsweredPurchased : AnsweredNotPurchased,
            };
            var written = Journal.TryAppend(answer);
            Invalidate();
            return written ? LedgerAnswer.Applied : LedgerAnswer.WriteFailed;
        }

        return LedgerAnswer.NoSuchAttempt;
    }

    // Ends every run that was authorized and never ended, so none of them offers to resume any more.
    public bool Discard(DateTime nowUtc)
    {
        var runs = Review.Runs;
        var written = true;
        for (var index = 0; index < runs.Length; index++)
        {
            var run = runs[index];
            if (run.Authorization is null || run.FinalState is not null)
            {
                continue;
            }

            written &= Journal.TryAppend(new RunEnded
            {
                Sequence = Journal.NextSequence,
                RunId = run.RunId,
                AtUtc = nowUtc,
                FinalState = RunState.Stopped,
                Reason = DiscardReason,
            });
        }

        Invalidate();
        return written;
    }

    public bool SetAside(DateTime nowUtc, out string? setAsidePath)
    {
        var moved = Journal.TrySetAside(nowUtc, out setAsidePath);
        Invalidate();
        return moved;
    }

    private JournalReview Load()
    {
        Revision++;
        var read = Journal.Read();
        if (read.Failed)
        {
            ReadError = read.Error;
            return JournalReview.Empty;
        }

        ReadError = null;
        return JournalReview.Build(read.Entries);
    }
}
