// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

/// <summary>Provides a set of extension methods for operations on <see cref="ICollection{T}"/>.</summary>
[PublicAPI]
public static class CollectionExtensions
{
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
    /// Checks whatever given collection object is null or has no item.
    /// </summary>
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
    /// <param name="items">Item to check and add</param>
    /// <typeparam name="T">Type of the items in the collection</typeparam>
    /// <returns>Returns the added items.</returns>
    public static IEnumerable<T> AddIfNotContains<T>(this ICollection<T> source, IEnumerable<T> items)
    {
        Argument.IsNotNull(source);

        var addedItems = new List<T>();

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
    /// Removes all items from the collection those satisfy the given <paramref name="predicate"/>.
    /// </summary>
    /// <typeparam name="T">Type of the items in the collection</typeparam>
    /// <param name="source">The collection</param>
    /// <param name="predicate">The condition to remove the items</param>
    /// <returns>List of removed items</returns>
    public static IList<T> RemoveAll<T>(this ICollection<T> source, Func<T, bool> predicate)
    {
        var items = source.Where(predicate).ToList();

        foreach (var item in items)
        {
            source.Remove(item);
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
