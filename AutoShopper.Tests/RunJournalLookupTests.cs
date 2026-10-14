using System.IO;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Exports;
using AutoShopper.Core.Ledger;
using static AutoShopper.Tests.ExecutionFixtures;

namespace AutoShopper.Tests;

public sealed class RunJournalLookupTests : IDisposable
{
    private static readonly Guid Attempt = new("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid OtherRun = new("eeeeeeee-0000-0000-0000-000000000002");

    private readonly TemporaryDirectory directory = new();

    public void Dispose() => directory.Dispose();

    [Fact]
    public void ARunStillInTheJournalIsFound()
    {
        var journal = new RunJournal(directory.Path);
        FinishedRun(journal, RunId);

        var lookup = RunJournal.FindRun(directory.Path, RunId, Now);

        Assert.Equal(JournalLookupStatus.Found, lookup.Status);
        Assert.Equal(5, lookup.Entries.Length);
        var progress = JournalReplayer.Replay(lookup.Entries, RunId);
        Assert.Equal(5_050, progress.ConfirmedSpend);
        Assert.Equal(RunState.Completed, progress.FinalState);
    }

    [Fact]
    public void ARotatedRunIsReadBackFromItsArchive()
    {
        var journal = new RunJournal(directory.Path);
        FinishedRun(journal, RunId);
        var rotation = journal.Rotate(Now.AddMinutes(30));
        Assert.Equal(1, rotation.ArchivedRuns);
        Assert.Empty(new RunJournal(directory.Path).Read().Entries);

        var lookup = RunJournal.FindRun(directory.Path, RunId, Now);

        Assert.Equal(JournalLookupStatus.Found, lookup.Status);
        Assert.Equal(5, lookup.Entries.Length);
        Assert.Equal(5_050, JournalReplayer.Replay(lookup.Entries, RunId).ConfirmedSpend);
    }

    [Fact]
    public void OnlyTheAskedRunIsGathered()
    {
        var journal = new RunJournal(directory.Path);
        FinishedRun(journal, OtherRun);
        FinishedRun(journal, RunId);

        var lookup = RunJournal.FindRun(directory.Path, RunId, Now);

        Assert.Equal(5, lookup.Entries.Length);
        for (var index = 0; index < lookup.Entries.Length; index++)
        {
            Assert.Equal(RunId, lookup.Entries[index]!.RunId);
        }
    }

    [Fact]
    public void ArchivesWrittenBeforeTheRunStartedAreNotRead()
    {
        var journal = new RunJournal(directory.Path);
        FinishedRun(journal, RunId);
        journal.Rotate(Now);

        var lookup = RunJournal.FindRun(directory.Path, RunId, Now.AddHours(2));

        Assert.Equal(JournalLookupStatus.Gone, lookup.Status);
        Assert.Empty(lookup.Entries);
    }

    [Fact]
    public void ARunNoFileHoldsIsGone()
    {
        var journal = new RunJournal(directory.Path);
        FinishedRun(journal, OtherRun);

        Assert.Equal(JournalLookupStatus.Gone, RunJournal.FindRun(directory.Path, RunId, Now).Status);
        Assert.Equal(JournalLookupStatus.Gone, RunJournal.FindRun(Path.Combine(directory.Path, "missing"), RunId, Now).Status);
        Assert.Equal(JournalLookupStatus.Gone, RunJournal.FindRun(directory.Path, RunId, DateTime.MinValue).Status);
    }

    [Fact]
    public void ARunSplitByASetAsideJournalIsGatheredWhole()
    {
        var journal = new RunJournal(directory.Path);
        var writer = new JournalWriter(journal).Authorize();
        writer.Intent(Attempt).Submitted(Attempt);
        Assert.True(journal.TrySetAside(Now.AddMinutes(5), out var setAsidePath));
        Assert.True(File.Exists(setAsidePath));
        writer.Confirmed(Attempt).Ended(RunState.Completed);

        var lookup = RunJournal.FindRun(directory.Path, RunId, Now);

        Assert.Equal(JournalLookupStatus.Found, lookup.Status);
        Assert.Equal(5, lookup.Entries.Length);
        var progress = JournalReplayer.Replay(lookup.Entries, RunId);
        Assert.NotNull(progress.Authorization);
        Assert.Equal(5_050, progress.ConfirmedSpend);
    }

    [Fact]
    public void ASummaryReadBackFromTheFileHoldsNoIdentifiers()
    {
        var journal = new RunJournal(directory.Path);
        var source = ExportFixtures.MixedRun();
        for (var index = 0; index < source.Entries.Count; index++)
        {
            Assert.True(journal.TryAppend(source.Entries[index]!), journal.LastError);
        }

        var lookup = RunJournal.FindRun(directory.Path, ExportFixtures.RunId, ExportFixtures.StartedAt);
        Assert.Equal(JournalLookupStatus.Found, lookup.Status);
        Assert.Contains(ExportFixtures.CharacterName, File.ReadAllText(journal.FilePath), StringComparison.Ordinal);

        var summary = RunSummaryBuilder.Build(ExportFixtures.RunId, lookup.Entries, ExportFixtures.Names());
        var text = RunSummaryExporter.ToText(summary);
        var json = RunSummaryExporter.ToJson(summary);

        Assert.Equal(2, summary.Purchases.Length);
        ExportFixtures.AssertNoIdentifiers(text);
        ExportFixtures.AssertNoIdentifiers(json);
    }

    private static void FinishedRun(RunJournal journal, Guid runId)
    {
        new JournalWriter(journal, runId).Authorize().Intent(Attempt).Submitted(Attempt).Confirmed(Attempt).Ended(RunState.Completed);
    }
}
