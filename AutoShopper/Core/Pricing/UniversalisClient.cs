using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Pricing.Universalis;

namespace AutoShopper.Core.Pricing;

// Reads current listings from Universalis. Network only: it never touches the game, and every answer it returns
// is an estimate the planner may use and a run must re-check against the live board.
internal sealed class UniversalisClient : IDisposable
{
    // One request may take this long; the server gives up on its own after 10 seconds and answers 504.
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    // A pooled connection is replaced after this long, so a changed DNS answer is picked up.
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(5);

    // The largest answer accepted; a region answer for 25 items stays far below it.
    private const long MaximumResponseBytes = 64L * 1024 * 1024;

    private const string JsonMediaType = "application/json";

    private readonly HttpClient http;
    private readonly RequestThrottle throttle;
    private readonly TimeProvider time;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Func<double> jitter;

    public UniversalisClient(string version)
        : this(CreateHandler(), version, TimeProvider.System, null, null)
    {
    }

    public UniversalisClient(
        HttpMessageHandler handler,
        string version,
        TimeProvider time,
        Func<TimeSpan, CancellationToken, Task>? delay,
        Func<double>? jitter)
    {
        this.time = time;
        this.delay = delay ?? ((span, token) => Task.Delay(span, time, token));
        this.jitter = jitter ?? Random.Shared.NextDouble;
        http = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri(UniversalisApi.BaseAddress),
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = MaximumResponseBytes,
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UniversalisApi.UserAgent(version));
        http.DefaultRequestHeaders.Accept.ParseAdd(JsonMediaType);
        throttle = new RequestThrottle(RequestThrottle.DefaultConcurrency, RequestThrottle.DefaultInterval, time, this.delay);
    }

    public void Dispose()
    {
        http.Dispose();
        throttle.Dispose();
    }

    public async Task<ChunkPrices[]> FetchAllAsync(ImmutableArray<PriceChunk> chunks, Action? chunkDone, CancellationToken cancellationToken)
    {
        var tasks = new Task<ChunkPrices>[chunks.Length];
        for (var index = 0; index < tasks.Length; index++)
        {
            tasks[index] = FetchReportingAsync(chunks[index], chunkDone, cancellationToken);
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public async Task<ChunkPrices> FetchAsync(PriceChunk chunk, CancellationToken cancellationToken)
    {
        var path = UniversalisApi.CurrentListingsPath(chunk);
        var failure = PriceFailure.RetriesExhausted;
        for (var attempt = 1; attempt <= RetryPolicy.MaximumAttempts; attempt++)
        {
            var outcome = await AttemptAsync(path, chunk, cancellationToken).ConfigureAwait(false);
            if (outcome.Result is { } result)
            {
                return result;
            }

            failure = outcome.Failure;
            if (attempt == RetryPolicy.MaximumAttempts)
            {
                break;
            }

            // The throttle refuses every start while a wait this long lasts, so the other requests fail at once too.
            if (!RetryPolicy.TryGetDelay(outcome.RetryAfter, attempt, jitter(), out var wait))
            {
                throttle.PauseFor(wait);
                return ChunkPrices.FailedWith(chunk, failure);
            }

            if (outcome.RateLimited)
            {
                throttle.PauseFor(wait);
            }

            await delay(wait, cancellationToken).ConfigureAwait(false);
        }

        return ChunkPrices.FailedWith(chunk, failure == PriceFailure.None ? PriceFailure.RetriesExhausted : failure);
    }

    private static SocketsHttpHandler CreateHandler() => new()
    {
        MaxConnectionsPerServer = RequestThrottle.DefaultConcurrency,
        PooledConnectionLifetime = ConnectionLifetime,
        AutomaticDecompression = DecompressionMethods.All,
    };

    private async Task<ChunkPrices> FetchReportingAsync(PriceChunk chunk, Action? chunkDone, CancellationToken cancellationToken)
    {
        var result = await FetchAsync(chunk, cancellationToken).ConfigureAwait(false);
        chunkDone?.Invoke();
        return result;
    }

    private async Task<Attempt> AttemptAsync(string path, PriceChunk chunk, CancellationToken cancellationToken)
    {
        using var lease = await throttle.EnterAsync(cancellationToken).ConfigureAwait(false);
        if (!lease.Granted)
        {
            return Attempt.Done(ChunkPrices.FailedWith(chunk, PriceFailure.ServerPaused));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await http.GetAsync(path, timeout.Token).ConfigureAwait(false);
            var statusCode = (int)response.StatusCode;
            switch (RetryPolicy.Classify(statusCode))
            {
                case ResponseClass.Success:
                    var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
                    return Attempt.Done(UniversalisTranslator.Translate(body, chunk));
                case ResponseClass.NotFound:
                    return Attempt.Done(ChunkPrices.FailedWith(chunk, PriceFailure.NotFound));
                case ResponseClass.Retry:
                    var rateLimited = response.StatusCode == HttpStatusCode.TooManyRequests;
                    return new Attempt(null, RetryAfterOf(response), rateLimited, rateLimited ? PriceFailure.RateLimited : PriceFailure.HttpError);
                default:
                    return Attempt.Done(ChunkPrices.FailedWith(chunk, PriceFailure.HttpError));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new Attempt(null, null, false, PriceFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return new Attempt(null, null, false, PriceFailure.HttpError);
        }
    }

    private TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        if (header?.Date is { } date)
        {
            return date - time.GetUtcNow();
        }

        return null;
    }

    private readonly record struct Attempt(ChunkPrices? Result, TimeSpan? RetryAfter, bool RateLimited, PriceFailure Failure)
    {
        public static Attempt Done(ChunkPrices result) => new(result, null, false, PriceFailure.None);
    }
}
