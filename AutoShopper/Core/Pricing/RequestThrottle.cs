using System.Threading;
using System.Threading.Tasks;

namespace AutoShopper.Core.Pricing;

// Caps how many requests are in flight and how often a new one starts, and holds every start back while the server
// has asked for a pause. Universalis allows 25 requests a second and 8 connections per address; the defaults stay
// well under both so other plugins on the same machine keep their share.
internal sealed class RequestThrottle : IDisposable
{
    public const int DefaultConcurrency = 4;

    // Ten starts a second.
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(100);

    private readonly SemaphoreSlim slots;
    private readonly TimeProvider time;
    private readonly long intervalTimestamps;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly object gate = new();
    private long nextStart;
    private long pausedUntil;

    public RequestThrottle(int concurrency, TimeSpan interval, TimeProvider time, Func<TimeSpan, CancellationToken, Task> delay)
    {
        slots = new SemaphoreSlim(concurrency, concurrency);
        this.time = time;
        this.delay = delay;
        intervalTimestamps = ToTimestamps(interval);
    }

    public async Task<Lease> EnterAsync(CancellationToken cancellationToken)
    {
        await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                var wait = Reserve();
                if (wait <= TimeSpan.Zero)
                {
                    return new Lease(slots);
                }

                await delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            slots.Release();
            throw;
        }
    }

    public void PauseFor(TimeSpan duration)
    {
        lock (gate)
        {
            var until = time.GetTimestamp() + ToTimestamps(duration);
            pausedUntil = Math.Max(pausedUntil, until);
        }
    }

    public void Dispose() => slots.Dispose();

    private TimeSpan Reserve()
    {
        lock (gate)
        {
            var now = time.GetTimestamp();
            var earliest = Math.Max(nextStart, pausedUntil);
            if (now >= earliest)
            {
                nextStart = now + intervalTimestamps;
                return TimeSpan.Zero;
            }

            return time.GetElapsedTime(now, earliest);
        }
    }

    private long ToTimestamps(TimeSpan span) => (long)(span.TotalSeconds * time.TimestampFrequency);

    public readonly struct Lease : IDisposable
    {
        private readonly SemaphoreSlim? slots;

        public Lease(SemaphoreSlim slots)
        {
            this.slots = slots;
        }

        public void Dispose() => slots?.Release();
    }
}
