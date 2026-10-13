using System.Diagnostics.CodeAnalysis;

namespace AutoShopper.Core.Api.Model;

internal sealed class RecentStore<T>
    where T : class
{
    private readonly int capacity;
    private readonly TimeSpan lifetime;
    private readonly List<Entry> entries;

    public RecentStore(int capacity, TimeSpan lifetime)
    {
        this.capacity = capacity;
        this.lifetime = lifetime;
        entries = new List<Entry>(capacity + 1);
    }

    public int Count => entries.Count;

    public void Add(Guid id, T value, DateTime nowUtc)
    {
        Prune(nowUtc);
        var existing = IndexOf(id);
        if (existing >= 0)
        {
            entries.RemoveAt(existing);
        }

        entries.Add(new Entry(id, nowUtc, value));
        if (entries.Count > capacity)
        {
            entries.RemoveRange(0, entries.Count - capacity);
        }
    }

    public bool TryGet(Guid id, DateTime nowUtc, [NotNullWhen(true)] out T? value)
    {
        Prune(nowUtc);
        var index = IndexOf(id);
        value = index < 0 ? null : entries[index].Value;
        return value is not null;
    }

    public T? NewestWhere(Func<T, bool> predicate, DateTime nowUtc)
    {
        Prune(nowUtc);
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            if (predicate(entries[index].Value))
            {
                return entries[index].Value;
            }
        }

        return null;
    }

    private void Prune(DateTime nowUtc)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            return;
        }

        var expired = 0;
        while (expired < entries.Count && nowUtc - entries[expired].AddedAtUtc > lifetime)
        {
            expired++;
        }

        if (expired > 0)
        {
            entries.RemoveRange(0, expired);
        }
    }

    private int IndexOf(Guid id)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private readonly record struct Entry(Guid Id, DateTime AddedAtUtc, T Value);
}
