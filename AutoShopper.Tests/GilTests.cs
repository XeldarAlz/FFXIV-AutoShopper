using AutoShopper.Core.Shared;

namespace AutoShopper.Tests;

public sealed class GilTests
{
    [Fact]
    public void AddReportsOverflowInsteadOfWrapping()
    {
        Assert.False(Gil.TryAdd(long.MaxValue, 1, out _));
        Assert.False(Gil.TryAdd(long.MinValue, -1, out _));
        Assert.True(Gil.TryAdd(40, 2, out var sum));
        Assert.Equal(42, sum);
    }

    [Fact]
    public void SubtractReportsOverflowInsteadOfWrapping()
    {
        Assert.False(Gil.TrySubtract(long.MinValue, 1, out _));
        Assert.False(Gil.TrySubtract(long.MaxValue, -1, out _));
        Assert.True(Gil.TrySubtract(5, 7, out var difference));
        Assert.Equal(-2, difference);
    }

    [Fact]
    public void MultiplyReportsOverflowInsteadOfWrapping()
    {
        Assert.False(Gil.TryMultiply(long.MaxValue, 2, out _));
        Assert.False(Gil.TryMultiply(long.MinValue, -1, out _));
        Assert.True(Gil.TryMultiply(3_000_000_000, 4, out var product));
        Assert.Equal(12_000_000_000, product);
    }

    [Theory]
    [InlineData(100, 5, 5)]
    [InlineData(105, 5, 5)]
    [InlineData(19, 5, 0)]
    [InlineData(20, 5, 1)]
    [InlineData(1999, 5, 99)]
    public void PercentFloorRoundsDown(long amount, int percent, long expected)
    {
        Assert.True(Gil.TryPercentFloor(amount, percent, out var portion));
        Assert.Equal(expected, portion);
    }

    [Fact]
    public void PercentFloorWorksAtTheTopOfTheRange()
    {
        Assert.True(Gil.TryPercentFloor(long.MaxValue, 5, out var portion));
        Assert.Equal(long.MaxValue / 20, portion);
        Assert.False(Gil.TryPercentFloor(long.MaxValue, 300, out _));
        Assert.False(Gil.TryPercentFloor(-1, 5, out _));
    }

    [Fact]
    public void StackPayableIsUnitTimesQuantityPlusFee()
    {
        Assert.True(Gil.TryStackPayable(1_000, 5, 250, out var payable));
        Assert.Equal(5_250, payable);
        Assert.False(Gil.TryStackPayable(1_000, 0, 0, out _));
        Assert.False(Gil.TryStackPayable(-1, 5, 0, out _));
        Assert.False(Gil.TryStackPayable(1_000, 5, -1, out _));
        Assert.False(Gil.TryStackPayable(long.MaxValue / 2, 3, 0, out _));
        Assert.False(Gil.TryStackPayable(long.MaxValue, 1, 1, out _));
    }

    [Fact]
    public void SaturatingMathClampsAtTheEdges()
    {
        Assert.Equal(long.MaxValue, Gil.SaturatingAdd(long.MaxValue, 5));
        Assert.Equal(long.MaxValue, Gil.SaturatingMultiply(long.MaxValue, 3));
        Assert.Equal(long.MinValue, Gil.SaturatingMultiply(long.MaxValue, -3));
        Assert.Equal(12, Gil.SaturatingMultiply(3, 4));
    }

    [Theory]
    [InlineData("1,234,567", 1_234_567)]
    [InlineData("1.000", 1_000)]
    [InlineData("12 345", 12_345)]
    [InlineData("  42 ", 42)]
    [InlineData("1'000'000", 1_000_000)]
    [InlineData("0", 0)]
    [InlineData("9223372036854775807", long.MaxValue)]
    public void ParseAcceptsPlainAndGroupedNumbers(string text, long expected)
    {
        Assert.True(Gil.TryParse(text, out var amount));
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("1,23")]
    [InlineData("1,0000")]
    [InlineData("1,,000")]
    [InlineData(",100")]
    [InlineData("100,")]
    [InlineData("12a")]
    [InlineData("1.5")]
    [InlineData("1234,567")]
    [InlineData("9223372036854775808")]
    public void ParseRejectsMalformedOrOverflowingText(string text)
    {
        Assert.False(Gil.TryParse(text, out var amount));
        Assert.Equal(0, amount);
    }
}
