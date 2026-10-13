using System.Collections.Immutable;
using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Core.Ledger;

// A null entry is a line that could not be read; when the file ends without a line feed the last entry is null and
// TornTail is set, which the replayer reports as a torn tail rather than as corruption.
internal sealed class JournalReadResult
{
    public required ImmutableArray<JournalEvent?> Entries { get; init; }

    public required bool Missing { get; init; }

    public required bool TornTail { get; init; }

    public required int UnreadableLines { get; init; }

    public required long LastSequence { get; init; }

    public string? Error { get; init; }

    public bool Failed => Error is not null;

    public static JournalReadResult Failure(string error)
    {
        return new JournalReadResult
        {
            Entries = [],
            Missing = false,
            TornTail = false,
            UnreadableLines = 0,
            LastSequence = 0,
            Error = error,
        };
    }
}
