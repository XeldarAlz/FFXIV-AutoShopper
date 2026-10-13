using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Exports;
using AutoShopper.Core.Items;
using AutoShopper.Core.Ledger;
using AutoShopper.Core.Tasks;

namespace AutoShopper.Core.Stats;

internal enum SummaryFormat : byte
{
    Text,
    Json,
}

internal enum SummaryStatus : byte
{
    Ready,
    NotRecorded,
    DryRunGone,
    JournalGone,
    ReadFailed,
}

internal readonly record struct SummaryExport(SummaryStatus Status, string Text);

// Summaries are made on request, on the framework thread, where the journal is written. A real run is read back from
// the purchase journal or the archive it rotated into. A dry run only ever lived in memory, so the last one is the only
// one that can be summarised, and only until the plugin reloads.
internal sealed class RunSummaries(string directoryPath, ItemCatalog catalog)
{
    private Guid dryRunId;
    private IRunJournal? dryRunJournal;

    // Runs that ended after this belong to the current session, which the Shop page's finished-run card is about.
    public DateTime LoadedAtUtc { get; } = DateTime.UtcNow;

    public void Track(ShopRunState run)
    {
        if (!run.DryRun)
        {
            return;
        }

        dryRunId = run.RunId;
        dryRunJournal = run.Journal;
    }

    // Says what can be tried without touching a file; a real run's journal is only read when its summary is asked for.
    public SummaryStatus Availability(RunRecord record)
    {
        if (record.RunId is not { } runId)
        {
            return SummaryStatus.NotRecorded;
        }

        return record.DryRun && (dryRunJournal is null || dryRunId != runId) ? SummaryStatus.DryRunGone : SummaryStatus.Ready;
    }

    public SummaryExport Export(RunRecord record, SummaryFormat format)
    {
        var availability = Availability(record);
        if (availability != SummaryStatus.Ready)
        {
            return new SummaryExport(availability, string.Empty);
        }

        var runId = record.RunId.GetValueOrDefault();
        if (record.DryRun)
        {
            return Export(runId, dryRun: true, dryRunJournal!.Read().Entries, format);
        }

        var lookup = RunJournal.FindRun(directoryPath, runId, record.StartedAtUtc);
        switch (lookup.Status)
        {
            case JournalLookupStatus.Found:
                return Export(runId, dryRun: false, lookup.Entries, format);
            case JournalLookupStatus.Gone:
                RunLog.Info($"Summary: no journal file holds run {runId} any more.");
                return new SummaryExport(SummaryStatus.JournalGone, string.Empty);
            default:
                RunLog.Warning($"Summary: the journal of run {runId} could not be read: {lookup.Error}.");
                return new SummaryExport(SummaryStatus.ReadFailed, string.Empty);
        }
    }

    // The run on screen, finished or still going: its own journal holds every event it wrote so far.
    public SummaryExport Export(ShopRunState run, SummaryFormat format)
    {
        var read = run.Journal.Read();
        if (read.Failed)
        {
            RunLog.Warning($"Summary: the journal of run {run.RunId} could not be read: {read.Error}.");
            return new SummaryExport(SummaryStatus.ReadFailed, string.Empty);
        }

        return Export(run.RunId, run.DryRun, read.Entries, format);
    }

    private SummaryExport Export(Guid runId, bool dryRun, IReadOnlyList<JournalEvent?> entries, SummaryFormat format)
    {
        var summary = RunSummaryBuilder.Build(runId, entries, dryRun, GameExportNames.For(catalog));
        var text = format == SummaryFormat.Json ? RunSummaryExporter.ToJson(summary) : RunSummaryExporter.ToText(summary);
        return new SummaryExport(SummaryStatus.Ready, text);
    }
}
