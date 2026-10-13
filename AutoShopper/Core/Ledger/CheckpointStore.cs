using System.IO;
using System.Text.Json;

namespace AutoShopper.Core.Ledger;

internal enum CheckpointSource : byte
{
    None = 0,
    Primary = 1,
    Backup = 2,
}

internal readonly record struct CheckpointLoad<T>(T? Value, CheckpointSource Source, bool PrimaryDamaged)
    where T : class;

// One JSON document replaced atomically: the new version is durable under a temporary name before it takes the
// file's place, and the version it replaced stays as the .bak, which a load falls back to when the file is damaged.
internal sealed class CheckpointStore<T>
    where T : class
{
    private const string BackupSuffix = ".bak";

    public CheckpointStore(string directoryPath, string fileName)
    {
        DirectoryPath = directoryPath;
        FilePath = Path.Combine(directoryPath, fileName);
        BackupPath = FilePath + BackupSuffix;
    }

    public string DirectoryPath { get; }

    public string FilePath { get; }

    public string BackupPath { get; }

    public string? LastError { get; private set; }

    public bool TrySave(T value)
    {
        byte[] content;
        try
        {
            content = JsonSerializer.SerializeToUtf8Bytes(value);
            Directory.CreateDirectory(DirectoryPath);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            LastError = exception.Message;
            return false;
        }

        if (!DurableFile.TryReplace(FilePath, content, BackupPath, out var error))
        {
            LastError = error;
            return false;
        }

        LastError = null;
        return true;
    }

    public CheckpointLoad<T> Load()
    {
        var primary = TryRead(FilePath, out var primaryExists);
        if (primary is not null)
        {
            return new CheckpointLoad<T>(primary, CheckpointSource.Primary, false);
        }

        var backup = TryRead(BackupPath, out _);
        if (backup is not null)
        {
            return new CheckpointLoad<T>(backup, CheckpointSource.Backup, primaryExists);
        }

        return new CheckpointLoad<T>(null, CheckpointSource.None, primaryExists);
    }

    private T? TryRead(string path, out bool exists)
    {
        if (!DurableFile.TryReadAll(path, out var content, out exists, out var error))
        {
            LastError = error;
            exists = true;
            return null;
        }

        if (!exists)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            LastError = exception.Message;
            return null;
        }
    }
}
