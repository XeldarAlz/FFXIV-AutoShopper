namespace AutoShopper.Core.Ledger;

internal readonly record struct JournalRotation(
    bool Succeeded,
    int ArchivedRuns,
    int KeptRuns,
    int ArchivedLines,
    int KeptLines,
    bool DroppedTornTail,
    string? ArchivePath,
    string? Error)
{
    public static JournalRotation Failure(string error)
    {
        return new JournalRotation(false, 0, 0, 0, 0, false, null, error);
    }
}
