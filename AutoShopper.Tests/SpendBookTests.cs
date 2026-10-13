using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Tests;

public sealed class SpendBookTests
{
    private static readonly Guid FirstAttempt = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondAttempt = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void ReservationHoldsBudgetUntilSettled()
    {
        var book = new SpendBook(10_000);

        Assert.Equal(SpendBookResult.Applied, book.TryReserve(FirstAttempt, 4_000));

        Assert.Equal(4_000, book.Reserved);
        Assert.Equal(6_000, book.Remaining);
        Assert.True(book.HasPendingAttempt);
        Assert.True(book.Snapshot().HasPendingAttempt);
    }

    [Fact]
    public void AnUncertainAttemptsReservationOnlyEverGrows()
    {
        var book = new SpendBook(10_000);
        book.TryReserve(FirstAttempt, 4_000);

        Assert.Equal(SpendBookResult.Applied, book.RaiseReservation(FirstAttempt, 4_500));
        Assert.Equal(4_500, book.Reserved);
        Assert.Equal(SpendBookResult.Applied, book.RaiseReservation(FirstAttempt, 3_000));
        Assert.Equal(4_500, book.Reserved);
        Assert.Equal(SpendBookResult.NoSuchAttempt, book.RaiseReservation(SecondAttempt, 9_000));
        Assert.Equal(4_500, book.Reserved);
    }

    [Fact]
    public void OnlyOneAttemptMayBePending()
    {
        var book = new SpendBook(10_000);
        book.TryReserve(FirstAttempt, 1_000);

        Assert.Equal(SpendBookResult.AttemptPending, book.TryReserve(SecondAttempt, 1_000));
        Assert.Equal(1_000, book.Reserved);
    }

    [Fact]
    public void ReservationBeyondTheRemainingBudgetIsRefused()
    {
        var book = new SpendBook(10_000);

        Assert.Equal(SpendBookResult.ExceedsRemaining, book.TryReserve(FirstAttempt, 10_001));
        Assert.False(book.HasPendingAttempt);
        Assert.Equal(SpendBookResult.Applied, book.TryReserve(FirstAttempt, 10_000));
        Assert.Equal(0, book.Remaining);
    }

    [Fact]
    public void ConfirmationMovesTheReservationToConfirmedSpendExactlyOnce()
    {
        var book = new SpendBook(10_000);
        book.TryReserve(FirstAttempt, 4_000);

        Assert.Equal(SpendBookResult.Applied, book.Confirm(FirstAttempt, 4_000));
        Assert.Equal(SpendBookResult.AlreadySettled, book.Confirm(FirstAttempt, 4_000));

        Assert.Equal(4_000, book.Confirmed);
        Assert.Equal(0, book.Reserved);
        Assert.Equal(6_000, book.Remaining);
        Assert.False(book.HasPendingAttempt);
    }

    [Fact]
    public void ConfirmedSpendUsesTheChargedAmount()
    {
        var book = new SpendBook(10_000);
        book.TryReserve(FirstAttempt, 4_000);

        book.Confirm(FirstAttempt, 4_100);

        Assert.Equal(4_100, book.Confirmed);
        Assert.Equal(5_900, book.Remaining);
    }

    [Fact]
    public void ReleaseFreesTheReservationWithoutSpending()
    {
        var book = new SpendBook(10_000);
        book.TryReserve(FirstAttempt, 4_000);

        Assert.Equal(SpendBookResult.Applied, book.Release(FirstAttempt));
        Assert.Equal(SpendBookResult.AlreadySettled, book.Release(FirstAttempt));
        Assert.Equal(SpendBookResult.AlreadySettled, book.Confirm(FirstAttempt, 4_000));
        Assert.Equal(SpendBookResult.AlreadySettled, book.TryReserve(FirstAttempt, 1));

        Assert.Equal(0, book.Confirmed);
        Assert.Equal(10_000, book.Remaining);
    }

    [Fact]
    public void UnknownAttemptsAreRejected()
    {
        var book = new SpendBook(10_000);
        book.TryReserve(FirstAttempt, 1_000);

        Assert.Equal(SpendBookResult.NoSuchAttempt, book.Confirm(SecondAttempt, 1_000));
        Assert.Equal(SpendBookResult.NoSuchAttempt, book.Release(SecondAttempt));
        Assert.Equal(SpendBookResult.InvalidAmount, book.TryReserve(Guid.Empty, 1));
        Assert.Equal(1_000, book.Reserved);
    }

    [Fact]
    public void InvalidAmountsAreRefused()
    {
        var book = new SpendBook(10_000);

        Assert.Equal(SpendBookResult.InvalidAmount, book.TryReserve(FirstAttempt, -1));
        book.TryReserve(FirstAttempt, 100);
        Assert.Equal(SpendBookResult.InvalidAmount, book.Confirm(FirstAttempt, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpendBook(-1));
    }

    [Fact]
    public void OverflowIsReportedInsteadOfWrapping()
    {
        var book = SpendBook.Restore(long.MaxValue, long.MaxValue - 10, FirstAttempt, 5, []);

        Assert.Equal(SpendBookResult.Overflow, book.Confirm(FirstAttempt, 20));
        Assert.Equal(long.MaxValue - 10, book.Confirmed);
        Assert.True(book.HasPendingAttempt);
    }

    [Fact]
    public void OverspendNeverReportsNegativeRemaining()
    {
        var book = new SpendBook(1_000);
        book.TryReserve(FirstAttempt, 1_000);
        book.Confirm(FirstAttempt, 1_200);

        Assert.True(book.IsOverspent);
        Assert.Equal(0, book.Remaining);
        Assert.Equal(SpendBookResult.ExceedsRemaining, book.TryReserve(SecondAttempt, 0));
    }

    [Fact]
    public void RestoreKeepsTheUnknownReservationAndSettledAttempts()
    {
        var book = SpendBook.Restore(10_000, 3_000, SecondAttempt, 2_000, [FirstAttempt]);

        Assert.Equal(3_000, book.Confirmed);
        Assert.Equal(2_000, book.Reserved);
        Assert.Equal(5_000, book.Remaining);
        Assert.Equal(SpendBookResult.AlreadySettled, book.Confirm(FirstAttempt, 1));
        Assert.Equal(SpendBookResult.AttemptPending, book.TryReserve(Guid.NewGuid(), 1));
    }
}
