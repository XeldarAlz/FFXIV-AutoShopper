namespace AutoShopper.Core.Pricing.Universalis;

internal enum ResponseClass : byte
{
    Success,
    NotFound,
    Retry,
    Fail,
}

// Bounded retries: a busy or failing server gets a few spaced attempts, never a loop.
internal static class RetryPolicy
{
    public const int MaximumAttempts = 4;

    public static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan MaximumBackoff = TimeSpan.FromSeconds(8);

    // A Retry-After longer than this gives up the request instead of holding the fetch open.
    public static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromSeconds(30);

    public static ResponseClass Classify(int statusCode)
    {
        if (statusCode is >= 200 and < 300)
        {
            return ResponseClass.Success;
        }

        if (statusCode == 404)
        {
            return ResponseClass.NotFound;
        }

        return statusCode is 408 or 429 or >= 500 ? ResponseClass.Retry : ResponseClass.Fail;
    }

    // Exponential backoff with equal jitter: half the step is fixed and half is random, so parallel requests that
    // failed together do not retry together. jitterSample is uniform in [0, 1).
    public static TimeSpan Backoff(int failedAttempts, double jitterSample)
    {
        var exponent = Math.Clamp(failedAttempts - 1, 0, 16);
        var step = Math.Min(BaseDelay.TotalMilliseconds * (1L << exponent), MaximumBackoff.TotalMilliseconds);
        var jitter = Math.Clamp(jitterSample, 0d, 1d);
        return TimeSpan.FromMilliseconds(step * 0.5 + step * 0.5 * jitter);
    }

    // The server's Retry-After wins over the backoff; false means the server asked for a wait this fetch will not hold.
    public static bool TryGetDelay(TimeSpan? retryAfter, int failedAttempts, double jitterSample, out TimeSpan delay)
    {
        if (retryAfter is not { } requested)
        {
            delay = Backoff(failedAttempts, jitterSample);
            return true;
        }

        delay = requested < TimeSpan.Zero ? TimeSpan.Zero : requested;
        return delay <= MaximumRetryAfter;
    }
}
