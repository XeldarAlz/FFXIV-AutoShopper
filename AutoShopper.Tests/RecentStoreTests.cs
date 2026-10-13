using AutoShopper.Core.Api.Model;

namespace AutoShopper.Tests;

public sealed class RecentStoreTests
{
    private static readonly DateTime Start = new(2026, 10, 13, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RunResultsKeepTheLastEight()
    {
        var store = new RecentStore<string>(ApiContract.RunResultCapacity, TimeSpan.Zero);
        var ids = Ids(10);

        for (var index = 0; index < ids.Length; index++)
        {
            store.Add(ids[index], Name(index), Start.AddHours(index));
        }

        Assert.Equal(8, store.Count);
        Assert.False(store.TryGet(ids[0], Start.AddDays(1), out _));
        Assert.False(store.TryGet(ids[1], Start.AddDays(1), out _));
        Assert.True(store.TryGet(ids[2], Start.AddDays(1), out var oldestKept));
        Assert.Equal(Name(2), oldestKept);
        Assert.True(store.TryGet(ids[9], Start.AddDays(30), out var newest));
        Assert.Equal(Name(9), newest);
    }

    [Fact]
    public void RunResultsNeverExpireWithTime()
    {
        var store = new RecentStore<string>(ApiContract.RunResultCapacity, TimeSpan.Zero);
        var id = Guid.NewGuid();

        store.Add(id, "run", Start);

        Assert.True(store.TryGet(id, Start.AddDays(365), out _));
    }

    [Fact]
    public void AQuoteIsDroppedOnceSixteenNewerQuotesExist()
    {
        var store = new RecentStore<string>(ApiContract.QuoteCapacity, ApiContract.QuoteLifetime);
        var ids = Ids(17);

        for (var index = 0; index < 16; index++)
        {
            store.Add(ids[index], Name(index), Start);
        }

        Assert.True(store.TryGet(ids[0], Start, out _));
        store.Add(ids[16], Name(16), Start);

        Assert.False(store.TryGet(ids[0], Start, out _));
        Assert.True(store.TryGet(ids[1], Start, out _));
        Assert.Equal(16, store.Count);
    }

    [Fact]
    public void AQuoteExpiresAfterTenMinutes()
    {
        var store = new RecentStore<string>(ApiContract.QuoteCapacity, ApiContract.QuoteLifetime);
        var early = Guid.NewGuid();
        var late = Guid.NewGuid();
        store.Add(early, "early", Start);
        store.Add(late, "late", Start.AddMinutes(5));

        Assert.True(store.TryGet(early, Start.AddMinutes(10), out _));
        Assert.False(store.TryGet(early, Start.AddMinutes(10).AddSeconds(1), out _));
        Assert.True(store.TryGet(late, Start.AddMinutes(15), out _));
        Assert.False(store.TryGet(late, Start.AddMinutes(16), out _));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void AddingAKnownIdReplacesItAsTheNewest()
    {
        var store = new RecentStore<string>(2, TimeSpan.Zero);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        store.Add(first, "first", Start);
        store.Add(second, "second", Start);
        store.Add(first, "first again", Start);

        store.Add(Guid.NewGuid(), "third", Start);

        Assert.True(store.TryGet(first, Start, out var replaced));
        Assert.Equal("first again", replaced);
        Assert.False(store.TryGet(second, Start, out _));
    }

    [Fact]
    public void NewestWhereLooksFromTheNewestEntry()
    {
        var store = new RecentStore<string>(4, TimeSpan.Zero);
        store.Add(Guid.NewGuid(), "apple", Start);
        store.Add(Guid.NewGuid(), "avocado", Start);
        store.Add(Guid.NewGuid(), "banana", Start);

        Assert.Equal("avocado", store.NewestWhere(static value => value.StartsWith('a'), Start));
        Assert.Null(store.NewestWhere(static value => value.StartsWith('z'), Start));
    }

    [Fact]
    public void ARemovedEntryIsNoLongerFound()
    {
        var store = new RecentStore<string>(ApiContract.QuoteCapacity, ApiContract.QuoteLifetime);
        var removed = Guid.NewGuid();
        var kept = Guid.NewGuid();
        store.Add(removed, "removed", Start);
        store.Add(kept, "kept", Start);

        Assert.True(store.Remove(removed));

        Assert.False(store.TryGet(removed, Start, out _));
        Assert.True(store.TryGet(kept, Start, out _));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void RemovingAnUnknownIdLeavesTheStoreAsItWas()
    {
        var store = new RecentStore<string>(ApiContract.QuoteCapacity, ApiContract.QuoteLifetime);
        store.Add(Guid.NewGuid(), "kept", Start);

        Assert.False(store.Remove(Guid.NewGuid()));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void AnUnknownIdIsNotFound()
    {
        var store = new RecentStore<string>(ApiContract.RunResultCapacity, TimeSpan.Zero);

        Assert.False(store.TryGet(Guid.NewGuid(), Start, out var value));
        Assert.Null(value);
    }

    private static Guid[] Ids(int count)
    {
        var ids = new Guid[count];
        for (var index = 0; index < count; index++)
        {
            ids[index] = Guid.NewGuid();
        }

        return ids;
    }

    private static string Name(int index) => $"entry {index}";
}
