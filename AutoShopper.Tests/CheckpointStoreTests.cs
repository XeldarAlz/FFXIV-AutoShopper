using System.IO;
using AutoShopper.Core.Ledger;

namespace AutoShopper.Tests;

public sealed class CheckpointStoreTests : IDisposable
{
    private const string FileName = "AutoShopper.Checkpoint.json";

    private readonly TemporaryDirectory directory = new();

    public void Dispose() => directory.Dispose();

    [Fact]
    public void SaveReplacesTheFileAndKeepsThePreviousVersionAsBackup()
    {
        var store = new CheckpointStore<Marker>(directory.Path, FileName);

        Assert.True(store.TrySave(new Marker("first", 1)));
        Assert.True(store.TrySave(new Marker("second", 2)));

        var load = store.Load();
        Assert.Equal(CheckpointSource.Primary, load.Source);
        Assert.Equal(new Marker("second", 2), load.Value);
        Assert.False(load.PrimaryDamaged);
        Assert.Contains("first", File.ReadAllText(store.BackupPath), StringComparison.Ordinal);
        Assert.False(File.Exists(store.FilePath + DurableFile.TemporarySuffix));
    }

    [Fact]
    public void DamagedFileFallsBackToTheBackup()
    {
        var store = new CheckpointStore<Marker>(directory.Path, FileName);
        store.TrySave(new Marker("first", 1));
        store.TrySave(new Marker("second", 2));
        File.WriteAllText(store.FilePath, "{\"Name\":\"sec");

        var load = store.Load();

        Assert.Equal(CheckpointSource.Backup, load.Source);
        Assert.True(load.PrimaryDamaged);
        Assert.Equal(new Marker("first", 1), load.Value);
    }

    [Fact]
    public void FailedSaveLeavesTheCurrentVersionWhole()
    {
        var store = new CheckpointStore<Marker>(directory.Path, FileName);
        store.TrySave(new Marker("first", 1));
        Directory.CreateDirectory(store.FilePath + DurableFile.TemporarySuffix);

        var saved = store.TrySave(new Marker("second", 2));

        Assert.False(saved);
        Assert.NotNull(store.LastError);
        Assert.Equal(new Marker("first", 1), store.Load().Value);
    }

    [Fact]
    public void LeftoverTemporaryFileFromACrashIsIgnoredAndOverwritten()
    {
        var store = new CheckpointStore<Marker>(directory.Path, FileName);
        store.TrySave(new Marker("first", 1));
        File.WriteAllText(store.FilePath + DurableFile.TemporarySuffix, "{\"Name\":\"torn");

        Assert.Equal(new Marker("first", 1), store.Load().Value);
        Assert.True(store.TrySave(new Marker("second", 2)));
        Assert.Equal(new Marker("second", 2), store.Load().Value);
    }

    [Fact]
    public void NothingSavedLoadsNothing()
    {
        var load = new CheckpointStore<Marker>(directory.Path, FileName).Load();

        Assert.Equal(CheckpointSource.None, load.Source);
        Assert.Null(load.Value);
        Assert.False(load.PrimaryDamaged);
    }

    public sealed record Marker(string Name, int Version);
}
