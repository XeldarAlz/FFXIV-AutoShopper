using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Execution.Model;

internal enum SpendBookResult : byte
{
    Applied = 0,
    AlreadySettled = 1,
    AttemptPending = 2,
    NoSuchAttempt = 3,
    InvalidAmount = 4,
    ExceedsRemaining = 5,
    Overflow = 6,
}

internal readonly record struct SpendState(long MaxSpend, long Confirmed, long Reserved, Guid PendingAttemptId)
{
    public bool HasPendingAttempt => PendingAttemptId != Guid.Empty || Reserved != 0;
}

internal sealed class SpendBook
{
    private readonly HashSet<Guid> settledAttempts = new();

    public SpendBook(long maxSpend)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxSpend);
        MaxSpend = maxSpend;
    }

    public long MaxSpend { get; }

    public long Confirmed { get; private set; }

    public long Reserved { get; private set; }

    public Guid PendingAttemptId { get; private set; }

    public bool HasPendingAttempt => PendingAttemptId != Guid.Empty;

    public long Remaining
    {
        get
        {
            if (!Gil.TrySubtract(MaxSpend, Confirmed, out var afterConfirmed) || !Gil.TrySubtract(afterConfirmed, Reserved, out var remaining))
            {
                return 0;
            }

            return Math.Max(0, remaining);
        }
    }

    public bool IsOverspent => Confirmed > MaxSpend;

    public static SpendBook Restore(long maxSpend, long confirmed, Guid pendingAttemptId, long reserved, ReadOnlySpan<Guid> settledAttemptIds)
    {
        var book = new SpendBook(maxSpend)
        {
            Confirmed = Math.Max(0, confirmed),
            Reserved = pendingAttemptId == Guid.Empty ? 0 : Math.Max(0, reserved),
            PendingAttemptId = pendingAttemptId,
        };
        for (var index = 0; index < settledAttemptIds.Length; index++)
        {
            book.settledAttempts.Add(settledAttemptIds[index]);
        }

        return book;
    }

    public SpendState Snapshot()
    {
        return new SpendState(MaxSpend, Confirmed, Reserved, PendingAttemptId);
    }

    public SpendBookResult TryReserve(Guid attemptId, long amount)
    {
        if (attemptId == Guid.Empty || amount < 0)
        {
            return SpendBookResult.InvalidAmount;
        }

        if (settledAttempts.Contains(attemptId))
        {
            return SpendBookResult.AlreadySettled;
        }

        if (HasPendingAttempt)
        {
            return SpendBookResult.AttemptPending;
        }

        if (!Gil.TrySubtract(MaxSpend, Confirmed, out var afterConfirmed))
        {
            return SpendBookResult.Overflow;
        }

        if (amount > afterConfirmed)
        {
            return SpendBookResult.ExceedsRemaining;
        }

        Reserved = amount;
        PendingAttemptId = attemptId;
        return SpendBookResult.Applied;
    }

    public SpendBookResult Confirm(Guid attemptId, long chargedAmount)
    {
        if (settledAttempts.Contains(attemptId))
        {
            return SpendBookResult.AlreadySettled;
        }

        if (attemptId == Guid.Empty || attemptId != PendingAttemptId)
        {
            return SpendBookResult.NoSuchAttempt;
        }

        if (chargedAmount < 0)
        {
            return SpendBookResult.InvalidAmount;
        }

        if (!Gil.TryAdd(Confirmed, chargedAmount, out var confirmed))
        {
            return SpendBookResult.Overflow;
        }

        Confirmed = confirmed;
        Settle(attemptId);
        return SpendBookResult.Applied;
    }

    // An uncertain purchase keeps its reservation; when the purse lost more than it, the reservation grows to match.
    public SpendBookResult RaiseReservation(Guid attemptId, long amount)
    {
        if (settledAttempts.Contains(attemptId))
        {
            return SpendBookResult.AlreadySettled;
        }

        if (attemptId == Guid.Empty || attemptId != PendingAttemptId)
        {
            return SpendBookResult.NoSuchAttempt;
        }

        if (amount < 0)
        {
            return SpendBookResult.InvalidAmount;
        }

        Reserved = Math.Max(Reserved, amount);
        return SpendBookResult.Applied;
    }

    public SpendBookResult Release(Guid attemptId)
    {
        if (settledAttempts.Contains(attemptId))
        {
            return SpendBookResult.AlreadySettled;
        }

        if (attemptId == Guid.Empty || attemptId != PendingAttemptId)
        {
            return SpendBookResult.NoSuchAttempt;
        }

        Settle(attemptId);
        return SpendBookResult.Applied;
    }

    private void Settle(Guid attemptId)
    {
        settledAttempts.Add(attemptId);
        Reserved = 0;
        PendingAttemptId = Guid.Empty;
    }
}
