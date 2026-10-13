namespace AutoShopper.Core.Stats;

public enum RunOutcome : byte
{
    Completed,
    CompletedPartial,
    Stopped,
    Failed,
    NeedsReconciliation,
}
