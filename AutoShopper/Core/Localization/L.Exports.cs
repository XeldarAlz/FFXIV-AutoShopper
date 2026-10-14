namespace AutoShopper.Core.Localization;

internal static partial class L
{
    internal static class Exports
    {
        public static readonly LocString CopySummary = new("export.copySummary", "Copy summary");
        public static readonly LocString CopyRunSummary = new("export.copyRunSummary", "Copy run summary");
        public static readonly LocString SummaryAsText = new("export.summaryAsText", "Copy summary as text");
        public static readonly LocString SummaryAsJson = new("export.summaryAsJson", "Copy summary as JSON");
        public static readonly LocString SummaryHelp = new("export.summaryHelp", "Items, worlds, quantities and gil of this run. Character names, content ids, retainers and listing ids are never in it.");
        public static readonly LocString SummaryCopied = new("export.summaryCopied", "Run summary copied to the clipboard");
        public static readonly LocString SummaryNotRecorded = new("export.summaryNotRecorded", "This run was recorded before run summaries were kept.");
        public static readonly LocString SummaryJournalGone = new("export.summaryJournalGone", "This run's purchase journal is gone, so its summary can no longer be made.");
        public static readonly LocString SummaryReadFailed = new("export.summaryReadFailed", "The purchase journal could not be read. The console has the details.");
        public static readonly LocString FinishedTitle = new("export.finishedTitle", "Run finished");

        public static readonly LocString Checklist = new("export.checklist", "Copy checklist");
        public static readonly LocString ChecklistHelp = new("export.checklistHelp", "Copies this plan as a shopping checklist: stops, items, quantities and estimates. Nothing about your character.");
        public static readonly LocString ChecklistAsText = new("export.checklistAsText", "Copy as text");
        public static readonly LocString ChecklistAsMarkdown = new("export.checklistAsMarkdown", "Copy as Markdown");
        public static readonly LocString ChecklistCopied = new("export.checklistCopied", "Checklist copied to the clipboard");
    }
}
