// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

[PublicAPI]
public static class HeadlessCollectionChangeDetectorExtensions
{
    /// <summary>
    /// Compares two collections by key and classifies their elements into added, removed, and existing (matched) items.
    /// Matching is O(n + m): <paramref name="newItems"/> is indexed by <paramref name="newKeySelector"/> once, then each
    /// old item is probed by <paramref name="oldKeySelector"/>.
    /// </summary>
    /// <typeparam name="TOld">The element type of <paramref name="oldItems"/>.</typeparam>
    /// <typeparam name="TNew">The element type of <paramref name="newItems"/>.</typeparam>
    /// <typeparam name="TKey">The key used to match an old item to a new item.</typeparam>
    /// <param name="oldItems">The original collection.</param>
    /// <param name="newItems">The new collection to compare against <paramref name="oldItems"/>.</param>
    /// <param name="oldKeySelector">Extracts the match key from an old item.</param>
    /// <param name="newKeySelector">Extracts the match key from a new item.</param>
    /// <param name="keyComparer">The comparer used to match keys, or <see langword="null"/> for the default comparer.</param>
    /// <returns>
    /// Items present only in <paramref name="newItems"/> (<c>AddedItems</c>), items present only in
    /// <paramref name="oldItems"/> (<c>RemovedItems</c>), and the matched old/new pairs present in both
    /// (<c>ExistItems</c>). Input order is preserved; on duplicate keys the first occurrence wins.
    /// </returns>
    [MustUseReturnValue]
    public static (
        IReadOnlyList<TNew> AddedItems,
        IReadOnlyList<TOld> RemovedItems,
        IReadOnlyList<(TOld Old, TNew New)> ExistItems
    ) DetectChanges<TOld, TNew, TKey>(
        this IReadOnlyCollection<TOld> oldItems,
        IReadOnlyCollection<TNew> newItems,
        Func<TOld, TKey> oldKeySelector,
        Func<TNew, TKey> newKeySelector,
        IEqualityComparer<TKey>? keyComparer = null
    )
        where TKey : notnull
    {
        var newByKey = _IndexByKey(newItems, newKeySelector, keyComparer);

        var removedItems = new List<TOld>();
        var existItems = new List<(TOld, TNew)>();
        var oldKeys = new HashSet<TKey>(oldItems.Count, keyComparer);

        foreach (var oldItem in oldItems)
        {
            var key = oldKeySelector(oldItem);

            // Skip duplicate old keys so a matched new item is not paired more than once (first occurrence wins).
            if (!oldKeys.Add(key))
            {
                continue;
            }

            if (newByKey.TryGetValue(key, out var newItem))
            {
                existItems.Add((oldItem, newItem));
            }
            else
            {
                removedItems.Add(oldItem);
            }
        }

        var addedItems = new List<TNew>();

        foreach (var (key, newItem) in newByKey)
        {
            if (!oldKeys.Contains(key))
            {
                addedItems.Add(newItem);
            }
        }

        return (addedItems, removedItems, existItems);
    }

    /// <summary>
    /// Compares two collections by key and classifies their elements into added, removed, updated, and unchanged items.
    /// Matching is O(n + m); <paramref name="hasChange"/> decides whether a matched pair has changed.
    /// </summary>
    /// <typeparam name="TOld">The element type of <paramref name="oldItems"/>.</typeparam>
    /// <typeparam name="TNew">The element type of <paramref name="newItems"/>.</typeparam>
    /// <typeparam name="TKey">The key used to match an old item to a new item.</typeparam>
    /// <param name="oldItems">The original collection.</param>
    /// <param name="newItems">The new collection to compare against <paramref name="oldItems"/>.</param>
    /// <param name="oldKeySelector">Extracts the match key from an old item.</param>
    /// <param name="newKeySelector">Extracts the match key from a new item.</param>
    /// <param name="hasChange">A predicate that determines whether a matched old/new pair differs.</param>
    /// <param name="keyComparer">The comparer used to match keys, or <see langword="null"/> for the default comparer.</param>
    /// <returns>
    /// Items present only in <paramref name="newItems"/> (<c>AddedItems</c>), items present only in
    /// <paramref name="oldItems"/> (<c>RemovedItems</c>), the matched pairs reported as changed (<c>UpdatedItems</c>),
    /// and the matched pairs reported as unchanged (<c>SameItems</c>). Input order is preserved.
    /// </returns>
    [MustUseReturnValue]
    public static (
        IReadOnlyList<TNew> AddedItems,
        IReadOnlyList<TOld> RemovedItems,
        IReadOnlyList<(TOld Old, TNew New)> UpdatedItems,
        IReadOnlyList<(TOld Old, TNew New)> SameItems
    ) DetectChanges<TOld, TNew, TKey>(
        this IReadOnlyCollection<TOld> oldItems,
        IReadOnlyCollection<TNew> newItems,
        Func<TOld, TKey> oldKeySelector,
        Func<TNew, TKey> newKeySelector,
        Func<TOld, TNew, bool> hasChange,
        IEqualityComparer<TKey>? keyComparer = null
    )
        where TKey : notnull
    {
        var newByKey = _IndexByKey(newItems, newKeySelector, keyComparer);

        var removedItems = new List<TOld>();
        var updatedItems = new List<(TOld, TNew)>();
        var sameItems = new List<(TOld, TNew)>();
        var oldKeys = new HashSet<TKey>(oldItems.Count, keyComparer);

        foreach (var oldItem in oldItems)
        {
            var key = oldKeySelector(oldItem);

            // Skip duplicate old keys so a matched new item is not paired more than once (first occurrence wins).
            if (!oldKeys.Add(key))
            {
                continue;
            }

            if (!newByKey.TryGetValue(key, out var newItem))
            {
                removedItems.Add(oldItem);
            }
            else if (hasChange(oldItem, newItem))
            {
                updatedItems.Add((oldItem, newItem));
            }
            else
            {
                sameItems.Add((oldItem, newItem));
            }
        }

        var addedItems = new List<TNew>();

        foreach (var (key, newItem) in newByKey)
        {
            if (!oldKeys.Contains(key))
            {
                addedItems.Add(newItem);
            }
        }

        return (addedItems, removedItems, updatedItems, sameItems);
    }

    /// <summary>Splits already-matched old/new pairs into updated and unchanged using <paramref name="hasChange"/>.</summary>
    /// <typeparam name="TOld">The type of the old item in each pair.</typeparam>
    /// <typeparam name="TNew">The type of the new item in each pair.</typeparam>
    /// <param name="existItems">The matched old/new pairs to inspect (e.g. the <c>ExistItems</c> of a prior call).</param>
    /// <param name="hasChange">A predicate that determines whether a pair differs.</param>
    /// <returns>
    /// The pairs reported as changed by <paramref name="hasChange"/> (<c>UpdatedItems</c>) and the pairs reported as
    /// unchanged (<c>SameItems</c>).
    /// </returns>
    [MustUseReturnValue]
    public static (
        IReadOnlyList<(TOld Old, TNew New)> UpdatedItems,
        IReadOnlyList<(TOld Old, TNew New)> SameItems
    ) DetectUpdates<TOld, TNew>(this IEnumerable<(TOld Old, TNew New)> existItems, Func<TOld, TNew, bool> hasChange)
    {
        var updatedItems = new List<(TOld, TNew)>();
        var sameItems = new List<(TOld, TNew)>();

        foreach (var (oldItem, newItem) in existItems)
        {
            if (hasChange(oldItem, newItem))
            {
                updatedItems.Add((oldItem, newItem));
            }
            else
            {
                sameItems.Add((oldItem, newItem));
            }
        }

        return (updatedItems, sameItems);
    }

    // Indexes items by key, keeping the first occurrence on duplicate keys (matches the prior first-match behavior).
    private static Dictionary<TKey, TItem> _IndexByKey<TItem, TKey>(
        IReadOnlyCollection<TItem> items,
        Func<TItem, TKey> keySelector,
        IEqualityComparer<TKey>? keyComparer
    )
        where TKey : notnull
    {
        var byKey = new Dictionary<TKey, TItem>(items.Count, keyComparer);

        foreach (var item in items)
        {
            byKey.TryAdd(keySelector(item), item);
        }

        return byKey;
    }
}
