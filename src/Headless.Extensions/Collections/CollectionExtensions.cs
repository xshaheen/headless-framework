// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

/// <summary>Extension methods for adding to and removing from generic collections.</summary>
[PublicAPI]
public static class HeadlessCollectionExtensions
{
    // Above this list size, a per-item List<T>.Contains scan (O(n)) is worth replacing with a one-time
    // HashSet snapshot so membership checks become O(1). Below it the snapshot allocation is not worthwhile.
    private const int _ContainsHashSetThreshold = 16;

    /// <summary>
    /// Adds the specified elements to the end of the collection.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the <paramref name="collection"/>.</typeparam>
    /// <param name="collection">The collection to which the elements should be added.</param>
    /// <param name="values">The elements to add to <paramref name="collection" />.</param>
    public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> values)
    {
        if (collection is List<T> list)
        {
            list.AddRange(values);

            return;
        }

        if (collection is ISet<T> set)
        {
            set.UnionWith(values);

            return;
        }

        foreach (var item in values)
        {
            collection.Add(item);
        }
    }

    /// <summary>
    /// Checks whether the given collection is <see langword="null"/> or has no items.
    /// </summary>
    /// <typeparam name="T">The type of the elements in <paramref name="source"/>.</typeparam>
    /// <param name="source">The collection to check.</param>
    /// <returns><see langword="true"/> if <paramref name="source"/> is <see langword="null"/> or empty; otherwise, <see langword="false"/>.</returns>
    public static bool IsNullOrEmpty<T>([NotNullWhen(false)] this ICollection<T>? source)
    {
        return source is null || source.Count == 0;
    }

    /// <summary>
    /// Adds an item to the collection if it's not already in the collection.
    /// </summary>
    /// <param name="source">The collection</param>
    /// <param name="item">Item to check and add</param>
    /// <typeparam name="T">Type of the items in the collection</typeparam>
    /// <returns>Returns True if added, returns False if not.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    public static bool AddIfNotContains<T>(this ICollection<T> source, T item)
    {
        Argument.IsNotNull(source);

        if (source.Contains(item))
        {
            return false;
        }

        source.Add(item);

        return true;
    }

    /// <summary>
    /// Adds items to the collection which are not already in the collection.
    /// </summary>
    /// <param name="source">The collection</param>
    /// <param name="items">Items to check and add if not already present.</param>
    /// <typeparam name="T">Type of the items in the collection</typeparam>
    /// <returns>Returns the added items.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    public static IEnumerable<T> AddIfNotContains<T>(this ICollection<T> source, IEnumerable<T> items)
    {
        Argument.IsNotNull(source);

        var addedItems = items.TryGetNonEnumeratedCount(out var count) ? new List<T>(count) : [];

        if (source is ISet<T> set)
        {
            foreach (var item in items)
            {
                if (set.Add(item))
                {
                    addedItems.Add(item);
                }
            }

            return addedItems;
        }

        // List<T>.Contains is O(n), making the naive loop O(n*m). For large lists, snapshot the existing
        // items into a HashSet (same default equality as List<T>.Contains) so membership checks are O(1).
        // Newly added items are tracked in the set too, preserving the de-duplication of repeated items.
        if (source is List<T> { Count: > _ContainsHashSetThreshold } list)
        {
            var seen = new HashSet<T>(list);

            foreach (var item in items)
            {
                if (seen.Add(item))
                {
                    list.Add(item);
                    addedItems.Add(item);
                }
            }

            return addedItems;
        }

        foreach (var item in items)
        {
            if (source.Contains(item))
            {
                continue;
            }

            source.Add(item);
            addedItems.Add(item);
        }

        return addedItems;
    }

    /// <summary>
    /// Adds an item to the collection if it's not already in the collection based on the given
    /// <paramref name="predicate"/>.
    /// </summary>
    /// <param name="source">The collection</param>
    /// <param name="predicate">The condition to decide if the item is already in the collection</param>
    /// <param name="itemFactory">A factory that returns the item</param>
    /// <typeparam name="T">Type of the items in the collection</typeparam>
    /// <returns>Returns True if added, returns False if not.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="source"/>, <paramref name="predicate"/>, or <paramref name="itemFactory"/> is <see langword="null"/>.
    /// </exception>
    public static bool AddIfNotContains<T>(this ICollection<T> source, Func<T, bool> predicate, Func<T> itemFactory)
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(predicate);
        Argument.IsNotNull(itemFactory);

        if (source.Any(predicate))
        {
            return false;
        }

        source.Add(itemFactory());

        return true;
    }

    /// <summary>
    /// Removes all items from the collection that satisfy the given <paramref name="predicate"/>.
    /// </summary>
    /// <typeparam name="T">Type of the items in the collection</typeparam>
    /// <param name="source">The collection</param>
    /// <param name="predicate">The condition to remove the items</param>
    /// <returns>List of removed items</returns>
    public static IList<T> RemoveAll<T>(this ICollection<T> source, Func<T, bool> predicate)
    {
        var items = source.Where(predicate).ToList();

        // List<T>.RemoveAll is a single O(n) compaction pass; the generic fallback is O(n*k) because each
        // ICollection<T>.Remove rescans from the start.
        if (source is List<T> list)
        {
            list.RemoveAll(new Predicate<T>(predicate));
        }
        else
        {
            foreach (var item in items)
            {
                source.Remove(item);
            }
        }

        return items;
    }

    /// <summary>
    /// Removes all items from the collection.
    /// </summary>
    /// <typeparam name="T">Type of the items in the collection</typeparam>
    /// <param name="source">The collection</param>
    /// <param name="items">Items to be removed from the list</param>
    public static void RemoveAll<T>(this ICollection<T> source, IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            source.Remove(item);
        }
    }
}
