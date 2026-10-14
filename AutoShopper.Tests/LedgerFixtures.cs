using System.IO;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Ledger;
using static AutoShopper.Tests.ExecutionFixtures;

namespace AutoShopper.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AutoShopperTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (!Directory.Exists(Path))
        {
            return;
        }

        var files = Directory.GetFiles(Path, "*", SearchOption.AllDirectories);
        for (var index = 0; index < files.Length; index++)
        {
            File.SetAttributes(files[index], FileAttributes.Normal);
        }

        Directory.Delete(Path, true);
    }
}

internal sealed class JournalWriter(RunJournal journal, Guid? runId = null)
{
    private readonly Guid runId = runId ?? RunId;

    public RunJournal Journal { get; } = journal;

    public JournalWriter Authorize()
    {
        Append(Authorization().ToJournalEvent(Journal.NextSequence, Now) with { RunId = runId });
        return this;
    }

    public JournalWriter Intent(Guid attemptId, ulong listingId = 77)
    {
        Append(new IntentRecorded
        {
            Sequence = Journal.NextSequence,
            RunId = runId,
            AtUtc = Now,
            AttemptId = attemptId,
            ItemId = Ore,
            Quality = OreDemand.Quality,
            ListingId = listingId,
            RetainerId = 501,
            WorldId = HomeWorld,
            IsHq = false,
            Quantity = 5,
            UnitPrice = 1_000,
            TotalTax = 50,
            Payable = 5_050,
            SearchGeneration = SearchGeneration,
            PlanVersion = 1,
            GilBefore = 60_000,
            OwnedBefore = 2,
        });
        return this;
    }

    public JournalWriter Submitted(Guid attemptId)
    {
        Append(new PurchaseSubmitted { Sequence = Journal.NextSequence, RunId = runId, AtUtc = Now, AttemptId = attemptId, Sent = true });
        return this;
    }

    public JournalWriter Confirmed(Guid attemptId)
    {
        Append(Confirmation(attemptId, Journal.NextSequence));
        return this;
    }

    public JournalWriter Unknown(Guid attemptId)
    {
        Append(new PurchaseUnknown { Sequence = Journal.NextSequence, RunId = runId, AtUtc = Now, AttemptId = attemptId, Reason = "test" });
        return this;
    }

    public JournalWriter Ended(RunState finalState)
    {
        Append(new RunEnded { Sequence = Journal.NextSequence, RunId = runId, AtUtc = Now, FinalState = finalState, Reason = "done" });
        return this;
    }

    public PurchaseConfirmed Confirmation(Guid attemptId, long sequence)
    {
        return new PurchaseConfirmed { Sequence = sequence, RunId = runId, AtUtc = Now, AttemptId = attemptId, Quantity = 5, Payable = 5_050, Evidence = "test" };
    }

    public void Append(JournalEvent journalEvent)
    {
        Assert.True(Journal.TryAppend(journalEvent), Journal.LastError);
    }
}
