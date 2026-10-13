using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AutoShopper.Core.Pricing;
using AutoShopper.Core.Pricing.Model;
using static AutoShopper.Tests.PricingFixtures;

namespace AutoShopper.Tests;

public sealed class UniversalisClientTests
{
    private const string Version = "0.1.0.0";

    [Fact]
    public async Task RateLimitIsHonouredBeforeTheRetry()
    {
        var clock = new ManualClock();
        var handler = new ScriptedHandler(
            _ => Respond(HttpStatusCode.TooManyRequests, retryAfterSeconds: 2),
            _ => Respond(HttpStatusCode.OK, Read("dc-multi-trimmed.json")));
        using var client = NewClient(handler, clock);

        var result = await client.FetchAsync(AetherChunk(), CancellationToken.None);

        Assert.False(result.Failed);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(TimeSpan.FromSeconds(2), clock.Delays);
        Assert.Equal(6, UniversalisTranslatorTests.Item(result, HighQualityItem).Offers.Length);
    }

    [Fact]
    public async Task RequestsCarryTheDescriptiveUserAgentAndTrimmedFields()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.OK, Read("dc-multi-trimmed.json")));
        using var client = NewClient(handler, new ManualClock());

        await client.FetchAsync(AetherChunk(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://universalis.app/api/v2/Aether/5057,2?entries=0&fields=itemIDs,unresolvedItems,items.itemID,items.lastUploadTime,items.worldUploadTimes,items.hasData,items.listings.listingID,items.listings.pricePerUnit,items.listings.quantity,items.listings.hq,items.listings.worldID,items.listings.onMannequin,items.listings.lastReviewTime",
            request.Uri);
        Assert.Equal("AutoShopper/0.1.0.0 (+https://github.com/XeldarAlz/FFXIV-AutoShopper)", request.UserAgent);
    }

    [Fact]
    public async Task NotFoundMeansNotMarketableOrUnknownScopeWithoutRetry()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.NotFound));
        using var client = NewClient(handler, new ManualClock());

        var result = await client.FetchAsync(AetherChunk(), CancellationToken.None);

        Assert.Equal(PriceFailure.NotFound, result.Failure);
        Assert.Single(handler.Requests);
        Assert.Equal(PriceCoverage.NotFound, UniversalisTranslatorTests.Item(result, FireShard).Coverage);
    }

    [Fact]
    public async Task ServerErrorsAreRetriedABoundedNumberOfTimes()
    {
        var clock = new ManualClock();
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.ServiceUnavailable));
        using var client = NewClient(handler, clock);

        var result = await client.FetchAsync(AetherChunk(), CancellationToken.None);

        Assert.Equal(PriceFailure.HttpError, result.Failure);
        Assert.Equal(Core.Pricing.Universalis.RetryPolicy.MaximumAttempts, handler.Requests.Count);
        Assert.Equal(PriceCoverage.RequestFailed, UniversalisTranslatorTests.Item(result, FireShard).Coverage);
    }

    [Fact]
    public async Task ARetryAfterLongerThanThePatienceEndsTheRequest()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.TooManyRequests, retryAfterSeconds: 600));
        using var client = NewClient(handler, new ManualClock());

        var result = await client.FetchAsync(AetherChunk(), CancellationToken.None);

        Assert.Equal(PriceFailure.RateLimited, result.Failure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ATransportErrorIsRetried()
    {
        var handler = new ScriptedHandler(
            _ => throw new HttpRequestException("reset"),
            _ => Respond(HttpStatusCode.OK, Read("dc-multi-trimmed.json")));
        using var client = NewClient(handler, new ManualClock());

        var result = await client.FetchAsync(AetherChunk(), CancellationToken.None);

        Assert.False(result.Failed);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task CancellationStopsTheFetch()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.OK, Read("dc-multi-trimmed.json")));
        using var client = NewClient(handler, new ManualClock());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchAsync(AetherChunk(), cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NoMoreThanFourRequestsAreInFlight()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.OK, Read("dc-multi-trimmed.json"))) { HoldFor = TimeSpan.FromMilliseconds(30) };
        using var client = NewClient(handler, new ManualClock());
        var chunks = ImmutableArray.CreateBuilder<PriceChunk>();
        for (var index = 0; index < 12; index++)
        {
            chunks.Add(AetherChunk());
        }

        var done = 0;
        var results = await client.FetchAllAsync(chunks.ToImmutable(), () => Interlocked.Increment(ref done), CancellationToken.None);

        Assert.Equal(12, results.Length);
        Assert.Equal(12, done);
        Assert.InRange(handler.MostInFlight, 1, RequestThrottle.DefaultConcurrency);
    }

    [Fact]
    public async Task StartsAreSpacedToTheRateLimit()
    {
        var clock = new ManualClock();
        using var throttle = new RequestThrottle(RequestThrottle.DefaultConcurrency, RequestThrottle.DefaultInterval, clock, clock.Delay);

        for (var index = 0; index < 3; index++)
        {
            using var lease = await throttle.EnterAsync(CancellationToken.None);
        }

        Assert.Equal([RequestThrottle.DefaultInterval, RequestThrottle.DefaultInterval], clock.Delays);
    }

    [Fact]
    public async Task APauseHoldsTheNextStart()
    {
        var clock = new ManualClock();
        using var throttle = new RequestThrottle(RequestThrottle.DefaultConcurrency, RequestThrottle.DefaultInterval, clock, clock.Delay);

        throttle.PauseFor(TimeSpan.FromSeconds(2));
        using (await throttle.EnterAsync(CancellationToken.None))
        {
        }

        Assert.Equal([TimeSpan.FromSeconds(2)], clock.Delays);
    }

    private static PriceChunk AetherChunk()
    {
        return Chunk(PriceQueryKind.DataCenter, "Aether", AetherWorlds, HighQualityItem, FireShard);
    }

    private static UniversalisClient NewClient(ScriptedHandler handler, ManualClock clock)
    {
        return new UniversalisClient(handler, Version, clock, clock.Delay, static () => 0.5);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, byte[]? body = null, int retryAfterSeconds = -1)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body ?? []) };
        if (retryAfterSeconds >= 0)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds));
        }

        return response;
    }

    private sealed record SeenRequest(string Uri, string UserAgent);

    private sealed class ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] script) : HttpMessageHandler
    {
        private readonly object gate = new();
        private int inFlight;

        public List<SeenRequest> Requests { get; } = [];

        public TimeSpan HoldFor { get; init; }

        public int MostInFlight { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int index;
            lock (gate)
            {
                index = Requests.Count;
                Requests.Add(new SeenRequest(request.RequestUri!.ToString(), request.Headers.UserAgent.ToString()));
                inFlight++;
                MostInFlight = Math.Max(MostInFlight, inFlight);
            }

            try
            {
                if (HoldFor > TimeSpan.Zero)
                {
                    await Task.Delay(HoldFor, cancellationToken);
                }

                return script[Math.Min(index, script.Length - 1)](request);
            }
            finally
            {
                lock (gate)
                {
                    inFlight--;
                }
            }
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly object gate = new();
        private long now;

        public List<TimeSpan> Delays { get; } = [];

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            lock (gate)
            {
                return now;
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(FetchedAt).AddTicks(GetTimestamp());
        }

        public Task Delay(TimeSpan span, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                Delays.Add(span);
                now += span.Ticks;
            }

            return Task.CompletedTask;
        }
    }
}
