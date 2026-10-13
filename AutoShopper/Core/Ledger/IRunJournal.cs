using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Core.Ledger;

// Where a run writes its events. A real run writes to the journal file; a dry run writes to memory, so nothing it did
// can count as spending after a restart.
internal interface IRunJournal
{
    long NextSequence { get; }

    string? LastError { get; }

    bool TryAppend(JournalEvent journalEvent);

    JournalReadResult Read();
}
