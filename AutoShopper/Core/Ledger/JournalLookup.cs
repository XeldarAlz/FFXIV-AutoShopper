using System.Collections.Immutable;
using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Core.Ledger;

internal enum JournalLookupStatus : byte
{
    Found = 0,
    Gone = 1,
    ReadFailed = 2,
}

// The events of one run gathered from the live journal and its archives; Gone means no file holds any of them any more.
internal readonly record struct JournalLookup(JournalLookupStatus Status, ImmutableArray<JournalEvent?> Entries, string? Error)
{
    public static JournalLookup Gone => new(JournalLookupStatus.Gone, [], null);

    public static JournalLookup Failure(string error) => new(JournalLookupStatus.ReadFailed, [], error);
}
