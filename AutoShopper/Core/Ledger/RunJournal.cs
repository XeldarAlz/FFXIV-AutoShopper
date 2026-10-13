using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;
using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Core.Ledger;

// Append-only JSON lines, one JournalEvent per line in the core's "Kind" shape. An append is on the disk before it
// returns true, so a caller that sends a purchase only after a true append never sends one the journal does not know.
// One owner appends, reads and rotates; the class is not thread safe.
internal sealed class RunJournal : IRunJournal
{
    public const string FileName = "AutoShopper.Journal.jsonl";

    private const string ArchivePrefix = "AutoShopper.Journal.";
    private const string SetAsideMarker = ".set-aside";
    private const string ArchiveExtension = ".jsonl";
    private const string ArchiveStampFormat = "yyyyMMdd-HHmmss";
    private const string BackupSuffix = ".bak";
    private const byte LineFeed = (byte)'\n';
    private const int TailScanChunk = 4_096;

    private static ReadOnlySpan<byte> LinePadding => " \t\r"u8;

    public RunJournal(string directoryPath)
    {
        DirectoryPath = directoryPath;
        FilePath = Path.Combine(directoryPath, FileName);
    }

    public string DirectoryPath { get; }

    public string FilePath { get; }

    public long LastSequence { get; private set; }

    public long NextSequence => LastSequence + 1;

    public string? LastError { get; private set; }

    public bool TryAppend(JournalEvent journalEvent)
    {
        if (!journalEvent.IsWellFormed())
        {
            LastError = $"refused a malformed {journalEvent.Kind} event (sequence {journalEvent.Sequence})";
            return false;
        }

        if (!TrySerializeLine(journalEvent, out var line))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            using var stream = DurableFile.OpenForAppend(FilePath);
            DropTornTail(stream);
            stream.Seek(0, SeekOrigin.End);
            stream.Write(line.WrittenSpan);
            stream.Flush(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastError = exception.Message;
            return false;
        }

        LastSequence = Math.Max(LastSequence, journalEvent.Sequence);
        LastError = null;
        return true;
    }

    public JournalReadResult Read()
    {
        if (!DurableFile.TryReadAll(FilePath, out var content, out var exists, out var error))
        {
            return JournalReadResult.Failure(error ?? "the journal could not be read");
        }

        var lines = ParseLines(content);
        var entries = ImmutableArray.CreateBuilder<JournalEvent?>(lines.Count);
        var unreadable = 0;
        var tornTail = false;
        long lastSequence = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!line.Terminated)
            {
                tornTail = true;
                entries.Add(null);
                continue;
            }

            if (line.Event is null)
            {
                unreadable++;
            }
            else
            {
                lastSequence = Math.Max(lastSequence, line.Event.Sequence);
            }

            entries.Add(line.Event);
        }

        LastSequence = Math.Max(LastSequence, lastSequence);
        return new JournalReadResult
        {
            Entries = entries.ToImmutable(),
            Missing = !exists,
            TornTail = tornTail,
            UnreadableLines = unreadable,
            LastSequence = lastSequence,
        };
    }

    // Moves every run that ended and needs no reconciliation into a dated archive next to the journal. A run with an
    // unresolved attempt, a run without an end, and any line that cannot be read stay in the journal; only a torn
    // tail, which no caller was ever told was written, is dropped. The archive is durable before the journal shrinks.
    public JournalRotation Rotate(DateTime nowUtc)
    {
        if (!DurableFile.TryReadAll(FilePath, out var content, out var exists, out var error))
        {
            return JournalRotation.Failure(error ?? "the journal could not be read");
        }

        if (!exists)
        {
            return new JournalRotation(true, 0, 0, 0, 0, false, null, null);
        }

        var lines = ParseLines(content);
        var runIds = new List<Guid>();
        var finished = FinishedRuns(lines, runIds);
        var tornTail = lines.Count > 0 && !lines[^1].Terminated;
        if (finished.Count == 0 && !tornTail)
        {
            return new JournalRotation(true, 0, runIds.Count, 0, lines.Count, false, null, null);
        }

        var archived = new ArrayBufferWriter<byte>();
        var kept = new ArrayBufferWriter<byte>();
        var archivedLines = 0;
        var keptLines = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!line.Terminated)
            {
                continue;
            }

            var archive = line.Event is not null && finished.Contains(line.Event.RunId);
            AppendLine(archive ? archived : kept, content.AsSpan(line.Start, line.Length));
            if (archive)
            {
                archivedLines++;
            }
            else
            {
                keptLines++;
            }
        }

        string? archivePath = null;
        if (archivedLines > 0)
        {
            archivePath = UniqueArchivePath(nowUtc);
            if (!DurableFile.TryReplace(archivePath, archived.WrittenSpan, null, out error))
            {
                return JournalRotation.Failure(error ?? "the archive could not be written");
            }
        }

        if (!DurableFile.TryReplace(FilePath, kept.WrittenSpan, FilePath + BackupSuffix, out error))
        {
            if (archivePath is not null)
            {
                DurableFile.TryDelete(archivePath);
            }

            return JournalRotation.Failure(error ?? "the journal could not be rewritten");
        }

        return new JournalRotation(true, finished.Count, runIds.Count - finished.Count, archivedLines, keptLines, tornTail, archivePath, null);
    }

    // Moves the whole journal to a dated file next to it, for a journal whose damage no answer can repair. The next
    // append starts a fresh journal; the moved file is kept for the player to look at.
    public bool TrySetAside(DateTime nowUtc, out string? setAsidePath)
    {
        setAsidePath = null;
        try
        {
            if (!File.Exists(FilePath))
            {
                return true;
            }

            var stem = ArchivePrefix + nowUtc.ToString(ArchiveStampFormat, CultureInfo.InvariantCulture) + SetAsideMarker;
            var path = Path.Combine(DirectoryPath, stem + ArchiveExtension);
            for (var suffix = 2; File.Exists(path); suffix++)
            {
                path = Path.Combine(DirectoryPath, $"{stem}-{suffix}{ArchiveExtension}");
            }

            File.Move(FilePath, path);
            setAsidePath = path;
            LastError = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastError = exception.Message;
            return false;
        }
    }

    private bool TrySerializeLine(JournalEvent journalEvent, out ArrayBufferWriter<byte> line)
    {
        line = new ArrayBufferWriter<byte>();
        try
        {
            using (var writer = new Utf8JsonWriter(line))
            {
                JsonSerializer.Serialize<JournalEvent>(writer, journalEvent);
            }
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            LastError = exception.Message;
            return false;
        }

        line.GetSpan(1)[0] = LineFeed;
        line.Advance(1);
        return true;
    }

    // A line without its line feed was never acknowledged to anyone, so it is cut away before the next line goes in:
    // gluing a new line onto it would turn a harmless torn tail into a corrupt entry in the middle of the journal.
    private static void DropTornTail(FileStream stream)
    {
        var length = stream.Length;
        Span<byte> chunk = stackalloc byte[TailScanChunk];
        var end = length;
        while (end > 0)
        {
            var start = Math.Max(0, end - TailScanChunk);
            var size = (int)(end - start);
            stream.Seek(start, SeekOrigin.Begin);
            stream.ReadExactly(chunk[..size]);
            var lastFeed = chunk[..size].LastIndexOf(LineFeed);
            if (lastFeed >= 0)
            {
                var keep = start + lastFeed + 1;
                if (keep != length)
                {
                    stream.SetLength(keep);
                }

                return;
            }

            end = start;
        }

        if (length > 0)
        {
            stream.SetLength(0);
        }
    }

    private static List<JournalLine> ParseLines(byte[] content)
    {
        var lines = new List<JournalLine>();
        var start = 0;
        while (start < content.Length)
        {
            var feed = content.AsSpan(start).IndexOf(LineFeed);
            var terminated = feed >= 0;
            var length = terminated ? feed : content.Length - start;
            var text = content.AsSpan(start, length).Trim(LinePadding);
            if (!text.IsEmpty)
            {
                lines.Add(new JournalLine(start, length, terminated, Deserialize(text)));
            }

            start += terminated ? length + 1 : length;
        }

        return lines;
    }

    private static JournalEvent? Deserialize(ReadOnlySpan<byte> text)
    {
        try
        {
            return JsonSerializer.Deserialize<JournalEvent>(text);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static HashSet<Guid> FinishedRuns(List<JournalLine> lines, List<Guid> runIds)
    {
        var eventsByRun = new Dictionary<Guid, List<JournalEvent?>>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!line.Terminated || line.Event is null)
            {
                continue;
            }

            if (!eventsByRun.TryGetValue(line.Event.RunId, out var events))
            {
                events = new List<JournalEvent?>();
                eventsByRun.Add(line.Event.RunId, events);
                runIds.Add(line.Event.RunId);
            }

            events.Add(line.Event);
        }

        var finished = new HashSet<Guid>();
        for (var index = 0; index < runIds.Count; index++)
        {
            var runId = runIds[index];
            var progress = JournalReplayer.Replay(eventsByRun[runId], runId);
            if (progress.Authorization is not null && progress.FinalState is not null && !progress.NeedsReconciliation)
            {
                finished.Add(runId);
            }
        }

        return finished;
    }

    private static void AppendLine(ArrayBufferWriter<byte> destination, ReadOnlySpan<byte> line)
    {
        destination.Write(line);
        destination.GetSpan(1)[0] = LineFeed;
        destination.Advance(1);
    }

    private string UniqueArchivePath(DateTime nowUtc)
    {
        var stem = ArchivePrefix + nowUtc.ToString(ArchiveStampFormat, CultureInfo.InvariantCulture);
        var path = Path.Combine(DirectoryPath, stem + ArchiveExtension);
        for (var suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Combine(DirectoryPath, $"{stem}-{suffix}{ArchiveExtension}");
        }

        return path;
    }

    private readonly record struct JournalLine(int Start, int Length, bool Terminated, JournalEvent? Event);
}
