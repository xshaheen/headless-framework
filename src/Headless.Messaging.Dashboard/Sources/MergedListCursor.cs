// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Where a list over several stores resumes: one keyset position per store, so each store pages on its own keyset,
/// which no write changes, and the merged list never repeats or skips a row.
/// </summary>
/// <remarks>
/// The text form is <c>s1|{store}={position}|…</c>, where a position is the store's own list position, <c>-</c> for a
/// store not read yet, and <c>!</c> for a store with no more rows. A store the cursor does not name starts at its
/// newest row; a store that did not answer keeps its position, so its rows come on a later page.
/// </remarks>
internal sealed class MergedListCursor
{
    private const string _FormatVersion = "s1";
    private const string _NotStarted = "-";
    private const string _Done = "!";

    private readonly Dictionary<string, (MessageListPosition? Position, bool Done)> _stores;

    private MergedListCursor(Dictionary<string, (MessageListPosition? Position, bool Done)> stores)
    {
        _stores = stores;
    }

    public bool IsDone(string store) => _stores.TryGetValue(store, out var state) && state.Done;

    public MessageListPosition? PositionOf(string store) =>
        _stores.TryGetValue(store, out var state) ? state.Position : null;

    /// <summary>
    /// Merges one page of each store, newest first by insert time, keeping each store's own order, and returns the
    /// first <paramref name="take"/> rows and how many it took from each store.
    /// </summary>
    public static (
        IReadOnlyList<(MessagingStore Store, MessageView View)> Rows,
        IReadOnlyDictionary<string, int> Taken
    ) Merge(IEnumerable<(MessagingStore Store, MessageListPage Page)> pages, int take)
    {
        var heads = pages.Select(static page => (page.Store, page.Page.Items, Index: 0)).ToArray();
        var rows = new List<(MessagingStore, MessageView)>(take);

        while (rows.Count < take)
        {
            var best = -1;

            for (var i = 0; i < heads.Length; i++)
            {
                if (
                    heads[i].Index < heads[i].Items.Count
                    && (best < 0 || heads[i].Items[heads[i].Index].Added > heads[best].Items[heads[best].Index].Added)
                )
                {
                    best = i;
                }
            }

            if (best < 0)
            {
                break;
            }

            rows.Add((heads[best].Store, heads[best].Items[heads[best].Index]));
            heads[best].Index++;
        }

        return (
            rows,
            heads.ToDictionary(
                static head => head.Store.Name,
                static head => head.Index,
                StringComparer.OrdinalIgnoreCase
            )
        );
    }

    /// <summary>
    /// Returns the cursor after a merged page, or <see langword="null"/> when no store of <paramref name="targets"/>
    /// has more rows.
    /// </summary>
    public MergedListCursor? Advance(
        IEnumerable<MessagingStore> targets,
        IEnumerable<(MessagingStore Store, MessageListPage Page)> pages,
        IReadOnlyDictionary<string, int> taken
    )
    {
        var next = new Dictionary<string, (MessageListPosition? Position, bool Done)>(StringComparer.OrdinalIgnoreCase);

        foreach (var store in targets)
        {
            next[store.Name] = _stores.TryGetValue(store.Name, out var state) ? state : (null, false);
        }

        foreach (var (store, page) in pages)
        {
            var count = taken.GetValueOrDefault(store.Name);
            var position =
                count > 0
                    ? new MessageListPosition(page.Items[count - 1].Added, page.Items[count - 1].StorageId)
                    : next[store.Name].Position;
            next[store.Name] = (position, count == page.Items.Count && page.Next is null);
        }

        return next.Values.All(static state => state.Done) ? null : new MergedListCursor(next);
    }

    public override string ToString()
    {
        var builder = new StringBuilder(_FormatVersion);

        foreach (var (name, (position, done)) in _stores)
        {
            builder.Append('|').Append(name).Append('=').Append(done ? _Done : position?.ToString() ?? _NotStarted);
        }

        return builder.ToString();
    }

    /// <summary>Reads a cursor; an empty value is the first page.</summary>
    public static bool TryParse(string? value, out MergedListCursor cursor)
    {
        var stores = new Dictionary<string, (MessageListPosition? Position, bool Done)>(
            StringComparer.OrdinalIgnoreCase
        );
        cursor = new MergedListCursor(stores);

        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        var parts = value.Split('|');

        if (!string.Equals(parts[0], _FormatVersion, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var part in parts.AsSpan(1))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0)
            {
                return false;
            }

            var text = part[(separator + 1)..];

            if (string.Equals(text, _Done, StringComparison.Ordinal))
            {
                stores[part[..separator]] = (null, true);
            }
            else if (string.Equals(text, _NotStarted, StringComparison.Ordinal))
            {
                stores[part[..separator]] = (null, false);
            }
            else if (MessageListPosition.TryParse(text, out var position))
            {
                stores[part[..separator]] = (position, false);
            }
            else
            {
                return false;
            }
        }

        return true;
    }
}
