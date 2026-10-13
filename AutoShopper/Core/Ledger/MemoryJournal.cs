using System.Collections.Immutable;
using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Core.Ledger;

internal sealed class MemoryJournal : IRunJournal
{
    private readonly List<JournalEvent> events = [];

    public long LastSequence { get; private set; }

    public long NextSequence => LastSequence + 1;

    public string? LastError { get; private set; }

    public int Count => events.Count;

    public bool TryAppend(JournalEvent journalEvent)
    {
        if (!journalEvent.IsWellFormed())
        {
            LastError = $"refused a malformed {journalEvent.Kind} event (sequence {journalEvent.Sequence})";
            return false;
        }

        events.Add(journalEvent);
        LastSequence = Math.Max(LastSequence, journalEvent.Sequence);
        LastError = null;
        return true;
    }

    public JournalReadResult Read()
    {
        var entries = ImmutableArray.CreateBuilder<JournalEvent?>(events.Count);
        for (var index = 0; index < events.Count; index++)
        {
            entries.Add(events[index]);
        }

        return new JournalReadResult
        {
            Entries = entries.MoveToImmutable(),
            Missing = events.Count == 0,
            TornTail = false,
            UnreadableLines = 0,
            LastSequence = LastSequence,
        };
    }
}
