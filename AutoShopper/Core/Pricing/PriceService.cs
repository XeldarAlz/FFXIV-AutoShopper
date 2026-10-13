using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Pricing.Universalis;

namespace AutoShopper.Core.Pricing;

internal enum PriceStartResult : byte
{
    Started,
    CoolingDown,
    NothingToPrice,
    NoWorlds,
}

// Owns the Universalis client and the newest price snapshot. A fetch only starts on request from the framework
// thread, runs on the thread pool, and a newer fetch or the plugin's unload cancels the one in flight.
internal sealed class PriceService : IDisposable
{
    // A manual fetch is allowed once in this window, so repeated clicks never turn into a stream of requests.
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    private readonly UniversalisClient client;
    private readonly object gate = new();
    private CancellationTokenSource? cancellation;
    private PriceSnapshot? latest;
    private long generation;
    private long lastStartMs = long.MinValue;
    private int completedRequests;
    private int totalRequests;
    private volatile bool fetching;
    private volatile bool disposed;

    public PriceService(string version)
    {
        client = new UniversalisClient(version);
    }

    public PriceSnapshot? Latest => Volatile.Read(ref latest);

    public bool Fetching => fetching;

    public int CompletedRequests => Volatile.Read(ref completedRequests);

    public int TotalRequests => Volatile.Read(ref totalRequests);

    public TimeSpan CooldownRemaining
    {
        get
        {
            var start = Volatile.Read(ref lastStartMs);
            if (start == long.MinValue)
            {
                return TimeSpan.Zero;
            }

            var remaining = Cooldown - TimeSpan.FromMilliseconds(Environment.TickCount64 - start);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public PriceStartResult TryStart(ImmutableArray<ScopeWorld> worlds, ImmutableArray<uint> itemIds)
    {
        if (CooldownRemaining > TimeSpan.Zero)
        {
            return PriceStartResult.CoolingDown;
        }

        if (worlds.IsDefaultOrEmpty)
        {
            return PriceStartResult.NoWorlds;
        }

        var chunks = UniversalisScope.Chunk(UniversalisScope.Plan(worlds), itemIds.AsSpan());
        if (chunks.IsEmpty)
        {
            return PriceStartResult.NothingToPrice;
        }

        CancellationToken token;
        long fetchGeneration;
        PriceSnapshot? previous;
        lock (gate)
        {
            if (disposed)
            {
                return PriceStartResult.NothingToPrice;
            }

            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            token = cancellation.Token;
            fetchGeneration = ++generation;
            previous = latest;
            Volatile.Write(ref completedRequests, 0);
            Volatile.Write(ref totalRequests, chunks.Length);
            Volatile.Write(ref lastStartMs, Environment.TickCount64);
            fetching = true;
        }

        RunLog.Info($"Fetching Universalis prices for {itemIds.Length} items on {worlds.Length} worlds in {chunks.Length} requests.");
        _ = Task.Run(() => FetchAsync(fetchGeneration, worlds, chunks, previous, token), token);
        return PriceStartResult.Started;
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
        }

        client.Dispose();
    }

    private async Task FetchAsync(long fetchGeneration, ImmutableArray<ScopeWorld> worlds, ImmutableArray<PriceChunk> chunks, PriceSnapshot? previous, CancellationToken token)
    {
        try
        {
            var results = await client.FetchAllAsync(chunks, () => ChunkDone(fetchGeneration), token).ConfigureAwait(false);
            var snapshot = PriceSnapshotBuilder.Build(fetchGeneration, DateTime.UtcNow, worlds, results, previous);
            lock (gate)
            {
                if (fetchGeneration != generation || disposed)
                {
                    return;
                }

                Volatile.Write(ref latest, snapshot);
                fetching = false;
            }

            RunLog.Info($"Prices ready: {snapshot.Offers.Length} listings for {snapshot.Items.Length} items; {snapshot.FailedRequestCount} of {snapshot.RequestCount} requests failed.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            EndFetch(fetchGeneration);
        }
        catch (Exception exception)
        {
            // Unloading disposes the client under a fetch in flight; that ending is expected and not logged.
            if (!disposed)
            {
                RunLog.Error(exception, "The price fetch failed");
            }

            EndFetch(fetchGeneration);
        }
    }

    private void ChunkDone(long fetchGeneration)
    {
        if (fetchGeneration == Volatile.Read(ref generation))
        {
            Interlocked.Increment(ref completedRequests);
        }
    }

    private void EndFetch(long fetchGeneration)
    {
        lock (gate)
        {
            if (fetchGeneration == generation)
            {
                fetching = false;
            }
        }
    }
}
