using System.IO;
using System.Text;
using System.Text.Json;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Ledger;
using static AutoShopper.Tests.ExecutionFixtures;

namespace AutoShopper.Tests;

public sealed class RunJournalTests : IDisposable
{
    private static readonly Guid FirstAttempt = new("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid SecondAttempt = new("cccccccc-0000-0000-0000-000000000002");

    private readonly TemporaryDirectory directory = new();

    public void Dispose() => directory.Dispose();

    [Fact]
    public void EventsRoundTripThroughTheFileInTheKindShape()
    {
        var journal = new RunJournal(directory.Path);
        var writer = new JournalWriter(journal).Authorize();
        writer.Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt);
        writer.Append(new Checkpoint { Sequence = journal.NextSequence, RunId = RunId, AtUtc = Now, State = RunState.Searching, CompletedWorldIds = [HomeWorld], ExcludedListingKeys = ["game:9"] });
        writer.Ended(RunState.Completed);

        var lines = File.ReadAllLines(journal.FilePath);
        var read = new RunJournal(directory.Path).Read();

        Assert.Equal(6, lines.Length);
        Assert.StartsWith("{\"Kind\":\"RunAuthorized\"", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("{\"Kind\":\"IntentRecorded\"", lines[1], StringComparison.Ordinal);
        Assert.False(read.TornTail);
        Assert.False(read.Missing);
        Assert.Equal(0, read.UnreadableLines);
        Assert.Equal(6, read.LastSequence);
        Assert.Equal(6, read.Entries.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            Assert.Equal(lines[index], JsonSerializer.Serialize(read.Entries[index]));
        }

        var progress = JournalReplayer.Replay(read.Entries);
        Assert.Equal(5_050, progress.ConfirmedSpend);
        Assert.Equal(RunState.Completed, progress.RecoveredState);
    }

    [Fact]
    public void MissingJournalReadsAsEmpty()
    {
        var read = new RunJournal(directory.Path).Read();

        Assert.True(read.Missing);
        Assert.False(read.Failed);
        Assert.Empty(read.Entries);
    }

    [Fact]
    public void TornTailIsReportedAndDroppedBeforeTheNextAppend()
    {
        var journal = new RunJournal(directory.Path);
        var writer = new JournalWriter(journal).Authorize();
        writer.Intent(FirstAttempt).Submitted(FirstAttempt);
        AppendRaw(journal.FilePath, "{\"Kind\":\"PurchaseConfirmed\",\"Sequence\":4,\"Run");

        var torn = new RunJournal(directory.Path).Read();

        Assert.True(torn.TornTail);
        Assert.Equal(4, torn.Entries.Length);
        Assert.Null(torn.Entries[^1]);
        var tornProgress = JournalReplayer.Replay(torn.Entries);
        Assert.True(tornProgress.TornTail);
        Assert.Equal(AttemptStatus.Unknown, Assert.Single(tornProgress.Attempts).Status);

        var reopened = new RunJournal(directory.Path);
        reopened.Read();
        new JournalWriter(reopened).Confirmed(FirstAttempt);
        var repaired = reopened.Read();

        Assert.False(repaired.TornTail);
        Assert.Equal(0, repaired.UnreadableLines);
        Assert.Equal(4, repaired.Entries.Length);
        var progress = JournalReplayer.Replay(repaired.Entries);
        Assert.Empty(progress.Anomalies);
        Assert.Equal(5_050, progress.ConfirmedSpend);
    }

    [Fact]
    public void CompleteLineWithoutItsLineFeedIsStillTorn()
    {
        var journal = new RunJournal(directory.Path);
        new JournalWriter(journal).Authorize().Intent(FirstAttempt);
        var unterminated = JsonSerializer.Serialize<JournalEvent>(new JournalWriter(journal).Confirmation(FirstAttempt, 3));
        AppendRaw(journal.FilePath, unterminated);

        var read = journal.Read();

        Assert.True(read.TornTail);
        Assert.Null(read.Entries[^1]);
        Assert.Equal(AttemptStatus.Unknown, Assert.Single(JournalReplayer.Replay(read.Entries).Attempts).Status);
    }

    [Fact]
    public void CrashBetweenIntentAndResultReplaysToUnknown()
    {
        var journal = new RunJournal(directory.Path);
        new JournalWriter(journal).Authorize().Intent(FirstAttempt).Submitted(FirstAttempt);

        var afterCrash = new RunJournal(directory.Path);
        var read = afterCrash.Read();
        var progress = JournalReplayer.Replay(read.Entries);

        Assert.Equal(3, afterCrash.LastSequence);
        Assert.Equal(4, afterCrash.NextSequence);
        var attempt = Assert.Single(progress.Attempts);
        Assert.Equal(AttemptStatus.Unknown, attempt.Status);
        Assert.Equal(5_050, progress.ReservedSpend);
        Assert.True(progress.NeedsReconciliation);
        Assert.Equal(RunState.NeedsReconciliation, progress.RecoveredState);
        Assert.Equal(FirstAttempt, progress.ToSpendBook().PendingAttemptId);
    }

    [Fact]
    public void DuplicateConfirmationIsAppliedOnce()
    {
        var journal = new RunJournal(directory.Path);
        var writer = new JournalWriter(journal).Authorize();
        writer.Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt).Confirmed(FirstAttempt);
        var repeated = writer.Confirmation(FirstAttempt, journal.LastSequence);
        writer.Append(repeated);

        var progress = JournalReplayer.Replay(new RunJournal(directory.Path).Read().Entries);

        Assert.Equal(5_050, progress.ConfirmedSpend);
        Assert.Equal(5, Assert.Single(progress.Demands, demand => demand.Demand.Key == OreDemand.Key).ConfirmedQuantity);
        Assert.False(progress.NeedsReconciliation);
    }

    [Fact]
    public void LockedFileFailsTheWriteAndLeavesTheJournalUnchanged()
    {
        var journal = new RunJournal(directory.Path);
        new JournalWriter(journal).Authorize();
        var before = File.ReadAllBytes(journal.FilePath);

        bool appended;
        using (new FileStream(journal.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            appended = journal.TryAppend(new JournalWriter(journal).Confirmation(FirstAttempt, journal.NextSequence));
        }

        Assert.False(appended);
        Assert.NotNull(journal.LastError);
        Assert.Equal(1, journal.LastSequence);
        Assert.Equal(before, File.ReadAllBytes(journal.FilePath));
    }

    [Fact]
    public void ReadOnlyFileFailsTheWrite()
    {
        var journal = new RunJournal(directory.Path);
        new JournalWriter(journal).Authorize();
        File.SetAttributes(journal.FilePath, FileAttributes.ReadOnly);

        var appended = journal.TryAppend(new JournalWriter(journal).Confirmation(FirstAttempt, journal.NextSequence));

        Assert.False(appended);
        Assert.Single(File.ReadAllLines(journal.FilePath));
    }

    [Fact]
    public void MalformedEventIsNeverWritten()
    {
        var journal = new RunJournal(directory.Path);

        var appended = journal.TryAppend(new JournalWriter(journal).Confirmation(FirstAttempt, 0));

        Assert.False(appended);
        Assert.False(File.Exists(journal.FilePath));
    }

    [Fact]
    public void CorruptLineInTheMiddleIsReadAsUnreadable()
    {
        var journal = new RunJournal(directory.Path);
        new JournalWriter(journal).Authorize();
        AppendRaw(journal.FilePath, "not json\n");
        new JournalWriter(journal).Intent(FirstAttempt);

        var read = journal.Read();

        Assert.Equal(1, read.UnreadableLines);
        Assert.False(read.TornTail);
        Assert.Null(read.Entries[1]);
        Assert.True(JournalReplayer.Replay(read.Entries).NeedsReconciliation);
    }

    [Fact]
    public void RotationArchivesFinishedRunsAndKeepsUnresolvedOnes()
    {
        var finishedRun = new Guid("dddddddd-0000-0000-0000-000000000001");
        var journal = new RunJournal(directory.Path);
        new JournalWriter(journal, finishedRun).Authorize().Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt).Ended(RunState.Completed);
        new JournalWriter(journal).Authorize().Intent(SecondAttempt, 78).Submitted(SecondAttempt).Ended(RunState.Stopped);
        AppendRaw(journal.FilePath, "{\"Kind\":\"Purch");

        var rotation = journal.Rotate(Now);

        Assert.True(rotation.Succeeded, rotation.Error);
        Assert.Equal(1, rotation.ArchivedRuns);
        Assert.Equal(1, rotation.KeptRuns);
        Assert.Equal(5, rotation.ArchivedLines);
        Assert.Equal(4, rotation.KeptLines);
        Assert.True(rotation.DroppedTornTail);
        Assert.True(File.Exists(rotation.ArchivePath));
        Assert.True(File.Exists(journal.FilePath + ".bak"));

        var archived = JournalReplayer.Replay(ReadLines(rotation.ArchivePath!), finishedRun);
        Assert.Equal(RunState.Completed, archived.RecoveredState);
        Assert.Equal(5_050, archived.ConfirmedSpend);

        var kept = journal.Read();
        Assert.False(kept.TornTail);
        var keptProgress = JournalReplayer.Replay(kept.Entries);
        Assert.Equal(RunId, keptProgress.RunId);
        Assert.True(keptProgress.NeedsReconciliation);
        Assert.Equal(AttemptStatus.Unknown, Assert.Single(keptProgress.Attempts).Status);
    }

    [Fact]
    public void RotationKeepsUnreadableLinesAndLeavesAnUnfinishedRun()
    {
        var journal = new RunJournal(directory.Path);
        new JournalWriter(journal).Authorize().Intent(FirstAttempt).Submitted(FirstAttempt).Confirmed(FirstAttempt);
        AppendRaw(journal.FilePath, "garbage\n");

        var rotation = journal.Rotate(Now);

        Assert.True(rotation.Succeeded);
        Assert.Equal(0, rotation.ArchivedRuns);
        Assert.Null(rotation.ArchivePath);
        Assert.Equal(5, File.ReadAllLines(journal.FilePath).Length);
    }

    [Fact]
    public void RotationNeverOverwritesAnEarlierArchive()
    {
        var journal = new RunJournal(directory.Path);
        new JournalWriter(journal).Authorize().Ended(RunState.Stopped);
        var first = journal.Rotate(Now);
        new JournalWriter(journal, Guid.NewGuid()).Authorize().Ended(RunState.Stopped);
        var second = journal.Rotate(Now);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.NotEqual(first.ArchivePath, second.ArchivePath);
        Assert.True(File.Exists(first.ArchivePath));
        Assert.True(File.Exists(second.ArchivePath));
        Assert.Empty(File.ReadAllLines(journal.FilePath));
    }

    private static void AppendRaw(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write);
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static List<JournalEvent?> ReadLines(string path)
    {
        var lines = File.ReadAllLines(path);
        var events = new List<JournalEvent?>(lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            events.Add(JsonSerializer.Deserialize<JournalEvent>(lines[index]));
        }

        return events;
    }
}
