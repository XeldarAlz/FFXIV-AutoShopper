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
    JournalGone,
    ReadFailed,
}

internal readonly record struct SummaryExport(SummaryStatus Status, string Text);

// Summaries are made on request, on the framework thread, where the journal is written. A run is read back from the
// purchase journal or the archive it rotated into.
internal sealed class RunSummaries(string directoryPath, ItemCatalog catalog)
{
    // Runs that ended after this belong to the current session, which the Shop page's finished-run card is about.
    public DateTime LoadedAtUtc { get; } = DateTime.UtcNow;

    // Says what can be tried without touching a file; a run's journal is only read when its summary is asked for.
    public SummaryStatus Availability(RunRecord record)
        => record.RunId is null ? SummaryStatus.NotRecorded : SummaryStatus.Ready;

    public SummaryExport Export(RunRecord record, SummaryFormat format)
    {
        var availability = Availability(record);
        if (availability != SummaryStatus.Ready)
        {
            return new SummaryExport(availability, string.Empty);
        }

        var runId = record.RunId.GetValueOrDefault();
        var lookup = RunJournal.FindRun(directoryPath, runId, record.StartedAtUtc);
        switch (lookup.Status)
        {
            case JournalLookupStatus.Found:
                return Export(runId, lookup.Entries, format);
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

        return Export(run.RunId, read.Entries, format);
    }

    private SummaryExport Export(Guid runId, IReadOnlyList<JournalEvent?> entries, SummaryFormat format)
    {
        var summary = RunSummaryBuilder.Build(runId, entries, GameExportNames.For(catalog));
        var text = format == SummaryFormat.Json ? RunSummaryExporter.ToJson(summary) : RunSummaryExporter.ToText(summary);
        return new SummaryExport(SummaryStatus.Ready, text);
    }
}
