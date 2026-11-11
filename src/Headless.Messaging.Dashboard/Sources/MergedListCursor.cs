// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Where a list over several stores resumes: the sort and filters it was read with, and one keyset position per store,
/// so each store pages on its own keyset, which no write changes, and the merged list never repeats or skips a row.
/// </summary>
/// <remarks>
/// The text form is <c>s2|{sort}{direction}|{filter digest}|{store}={position}|…</c>, where a position is the store's
/// own list position, <c>-</c> for a store not read yet, and <c>!</c> for a store with no more rows. A store the cursor
/// does not name starts at its first row; a store that did not answer keeps its position, so its rows come on a later
/// page. The filters travel as a digest, so the cursor stays short and never carries search text.
/// </remarks>
internal sealed class MergedListCursor
{
    private const string _FormatVersion = "s2";
    private const string _NotStarted = "-";
    private const string _Done = "!";
    private const char _Ascending = 'a';
    private const char _Descending = 'd';

    private static readonly SearchValues<char> _LowerHexDigits = SearchValues.Create("0123456789abcdef");

    private readonly Dictionary<string, (MessageListPosition? Position, bool Done)> _stores;

    // The sort and filter digest the positions were read under; null for the first page, which fits every query.
    private readonly string? _scope;

    private MergedListCursor(Dictionary<string, (MessageListPosition? Position, bool Done)> stores, string? scope)
    {
        _stores = stores;
        _scope = scope;
    }

    public bool IsDone(string store) => _stores.TryGetValue(store, out var state) && state.Done;

    public MessageListPosition? PositionOf(string store) =>
        _stores.TryGetValue(store, out var state) ? state.Position : null;

    /// <summary>
    /// Returns whether this cursor was read with the sort, direction, and filters of <paramref name="query"/>, so its
    /// positions mean the same in it. The first page's cursor fits every query.
    /// </summary>
    public bool Fits(MessageListQuery query) =>
        _scope is null || string.Equals(_scope, _ScopeOf(query), StringComparison.Ordinal);

    /// <summary>
    /// Merges one page of each store in <paramref name="query"/>'s order, keeping each store's own order, and returns
    /// the first <paramref name="take"/> rows and how many it took from each store.
    /// </summary>
    public static (
        IReadOnlyList<(MessagingStore Store, MessageView View)> Rows,
        IReadOnlyDictionary<string, int> Taken
    ) Merge(IEnumerable<(MessagingStore Store, MessageListPage Page)> pages, MessageListQuery query, int take)
    {
        var heads = pages.Select(static page => (page.Store, page.Page.Items, Index: 0)).ToArray();
        var rows = new List<(MessagingStore, MessageView)>(take);
        var sign = query.Descending ? -1 : 1;

        // Each store already returns its rows in order, so the merge only picks the head that comes first. Names
        // compare ordinally here; a store whose collation orders names otherwise still keeps its own order.
        int compare(MessageView left, MessageView right) =>
            sign
            * (
                query.SortBy == MessageListSortKey.Name
                    ? string.CompareOrdinal(left.Name, right.Name)
                    : left.Added.CompareTo(right.Added)
            );

        while (rows.Count < take)
        {
            var best = -1;

            for (var i = 0; i < heads.Length; i++)
            {
                if (
                    heads[i].Index < heads[i].Items.Count
                    && (best < 0 || compare(heads[i].Items[heads[i].Index], heads[best].Items[heads[best].Index]) < 0)
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
    /// Returns the cursor after a merged page of <paramref name="query"/>, or <see langword="null"/> when no store of
    /// <paramref name="targets"/> has more rows.
    /// </summary>
    public MergedListCursor? Advance(
        MessageListQuery query,
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
            var position = count > 0 ? MessageListPosition.Of(page.Items[count - 1]) : next[store.Name].Position;
            next[store.Name] = (position, count == page.Items.Count && page.Next is null);
        }

        return next.Values.All(static state => state.Done) ? null : new MergedListCursor(next, _ScopeOf(query));
    }

    public override string ToString()
    {
        var builder = new StringBuilder(_FormatVersion).Append('|').Append(_scope);

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
        cursor = new MergedListCursor(stores, scope: null);

        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        var parts = value.Split('|');

        if (
            parts.Length < 2
            || !string.Equals(parts[0], _FormatVersion, StringComparison.Ordinal)
            || !_IsScope(parts[1])
        )
        {
            return false;
        }

        foreach (var part in parts.AsSpan(2))
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

        cursor = new MergedListCursor(stores, parts[1]);

        return true;
    }

    // "{sort}{direction}.{digest}": the sort key's value, a or d, and 16 hex digits of the filters' digest.
    private static bool _IsScope(string value)
    {
        var dot = value.IndexOf('.', StringComparison.Ordinal);

        return dot >= 2
            && int.TryParse(value.AsSpan(0, dot - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var sortBy)
            && Enum.IsDefined((MessageListSortKey)sortBy)
            && value[dot - 1] is _Ascending or _Descending
            && value.Length - dot - 1 == 16
            && !value.AsSpan(dot + 1).ContainsAnyExcept(_LowerHexDigits);
    }

    // A digest rather than the filters themselves keeps the cursor short and the search text out of it. Every value is
    // written in an invariant form, so each node of a cluster digests one query alike.
    private static string _ScopeOf(MessageListQuery query)
    {
        var builder = new StringBuilder();

        void add(string? value)
        {
            // A length prefix, so no value can run into the next.
            builder
                .Append(value?.Length.ToString(CultureInfo.InvariantCulture) ?? "n")
                .Append('|')
                .Append(value)
                .Append('|');
        }

        add(((int)query.MessageType).ToString(CultureInfo.InvariantCulture));
        add(query.Status?.ToString("D"));
        add(query.TerminalOnly ? "1" : null);
        add(query.ConsumerIdentity);
        add(query.Name);
        add(query.Lane?.ToString("D"));
        add(query.TenantId);
        add(query.MessageId);
        add(query.FailureFingerprint);
        add(query.IsHeld?.ToString(CultureInfo.InvariantCulture));
        add(query.AddedFrom?.UtcTicks.ToString(CultureInfo.InvariantCulture));
        add(query.AddedBefore?.UtcTicks.ToString(CultureInfo.InvariantCulture));
        add(string.IsNullOrEmpty(query.Content) ? null : query.Content);
        add(string.IsNullOrEmpty(query.CorrelationId) ? null : query.CorrelationId);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)query.SortBy}{(query.Descending ? _Descending : _Ascending)}.{Convert.ToHexStringLower(digest, 0, 8)}"
        );
    }
}
