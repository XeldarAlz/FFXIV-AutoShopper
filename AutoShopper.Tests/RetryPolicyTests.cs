using AutoShopper.Core.Pricing.Universalis;

namespace AutoShopper.Tests;

public sealed class RetryPolicyTests
{
    // 0 success, 1 not found, 2 retry, 3 fail.
    [Theory]
    [InlineData(200, 0)]
    [InlineData(404, 1)]
    [InlineData(408, 2)]
    [InlineData(429, 2)]
    [InlineData(500, 2)]
    [InlineData(504, 2)]
    [InlineData(400, 3)]
    [InlineData(403, 3)]
    public void StatusCodesAreClassified(int statusCode, byte expected)
    {
        Assert.Equal((ResponseClass)expected, RetryPolicy.Classify(statusCode));
    }

    [Fact]
    public void RetryAfterWinsOverTheBackoff()
    {
        Assert.True(RetryPolicy.TryGetDelay(TimeSpan.FromSeconds(3), 1, 0.9, out var delay));

        Assert.Equal(TimeSpan.FromSeconds(3), delay);
    }

    [Fact]
    public void RetryAfterPastThePatienceGivesUp()
    {
        Assert.False(RetryPolicy.TryGetDelay(RetryPolicy.MaximumRetryAfter + TimeSpan.FromSeconds(1), 1, 0.5, out _));
    }

    [Theory]
    [InlineData(1, 250, 500)]
    [InlineData(2, 500, 1000)]
    [InlineData(3, 1000, 2000)]
    [InlineData(10, 4000, 8000)]
    public void BackoffDoublesWithinJitterAndStaysBounded(int failedAttempts, double lowest, double highest)
    {
        var low = RetryPolicy.Backoff(failedAttempts, 0);
        var high = RetryPolicy.Backoff(failedAttempts, 0.999999);

        Assert.Equal(lowest, low.TotalMilliseconds, 1);
        Assert.InRange(high.TotalMilliseconds, lowest, highest);
        Assert.True(high <= RetryPolicy.MaximumBackoff);
    }
}
