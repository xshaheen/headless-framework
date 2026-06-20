// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

[PublicAPI]
public static class CollectionChangeDetectorExtensions
{
    /// <summary>
    /// Compares two collections and classifies their elements into added, removed, and existing items using
    /// <paramref name="areSameEntity"/> to match an old item to a new item.
    /// </summary>
    /// <typeparam name="T1">The type of the elements in <paramref name="oldItems"/>.</typeparam>
    /// <typeparam name="T2">The type of the elements in <paramref name="newItems"/>.</typeparam>
    /// <param name="oldItems">The original collection.</param>
    /// <param name="newItems">The new collection to compare against <paramref name="oldItems"/>.</param>
    /// <param name="areSameEntity">A predicate that determines whether an old item and a new item represent the same entity.</param>
    /// <returns>
    /// A tuple containing the items present only in <paramref name="newItems"/> (<c>AddedItems</c>), the items present
    /// only in <paramref name="oldItems"/> (<c>RemovedItems</c>), and the matched old/new pairs present in both (<c>ExistItems</c>).
    /// </returns>
    [MustUseReturnValue]
    public static (List<T2> AddedItems, List<T1> RemovedItems, List<(T1, T2)> ExistItems) DetectChanges<T1, T2>(
        this IReadOnlyCollection<T1> oldItems,
        IReadOnlyCollection<T2> newItems,
        Func<T1, T2, bool> areSameEntity
    )
    {
        var addedItems = new List<T2>();
        var removedItems = new List<T1>();
        var existItems = new List<(T1, T2)>();

        foreach (var oldItem in oldItems)
        {
            var newItem = newItems.FirstOrDefault(newItem => areSameEntity(oldItem, newItem));

            if (newItem is null)
            {
                removedItems.Add(oldItem);

                continue;
            }

            existItems.Add((oldItem, newItem));
        }

        foreach (var newItem in newItems)
        {
            if (oldItems.All(oldItem => !areSameEntity(oldItem, newItem)))
            {
                addedItems.Add(newItem);
            }
        }

        return (addedItems, removedItems, existItems);
    }

    /// <summary>
    /// Compares two collections and classifies their elements into added, removed, updated, and unchanged items.
    /// <paramref name="areSameEntity"/> matches an old item to a new item, and <paramref name="hasChange"/> decides
    /// whether a matched pair has changed.
    /// </summary>
    /// <typeparam name="T1">The type of the elements in <paramref name="oldItems"/>.</typeparam>
    /// <typeparam name="T2">The type of the elements in <paramref name="newItems"/>.</typeparam>
    /// <param name="oldItems">The original collection.</param>
    /// <param name="newItems">The new collection to compare against <paramref name="oldItems"/>.</param>
    /// <param name="areSameEntity">A predicate that determines whether an old item and a new item represent the same entity.</param>
    /// <param name="hasChange">A predicate that determines whether a matched old/new pair differs.</param>
    /// <returns>
    /// A tuple containing the items present only in <paramref name="newItems"/> (<c>AddedItems</c>), the items present
    /// only in <paramref name="oldItems"/> (<c>RemovedItems</c>), the matched pairs reported as changed by
    /// <paramref name="hasChange"/> (<c>UpdatedItems</c>), and the matched pairs reported as unchanged (<c>SameItems</c>).
    /// </returns>
    [MustUseReturnValue]
    public static (
        List<T2> AddedItems,
        List<T1> RemovedItems,
        List<(T1, T2)> UpdatedItems,
        List<(T1, T2)> SameItems
    ) DetectChanges<T1, T2>(
        this IReadOnlyCollection<T1> oldItems,
        IReadOnlyCollection<T2> newItems,
        Func<T1, T2, bool> areSameEntity,
        Func<T1, T2, bool> hasChange
    )
    {
        var addedItems = new List<T2>();
        var removedItems = new List<T1>();
        var updatedItems = new List<(T1, T2)>();
        var sameItems = new List<(T1, T2)>();

        foreach (var oldItem in oldItems)
        {
            var newItem = newItems.FirstOrDefault(newItem => areSameEntity(oldItem, newItem));

            if (newItem is null)
            {
                removedItems.Add(oldItem);

                continue;
            }

            if (hasChange(oldItem, newItem))
            {
                updatedItems.Add((oldItem, newItem));
            }
            else
            {
                sameItems.Add((oldItem, newItem));
            }
        }

        foreach (var newItem in newItems)
        {
            if (oldItems.All(oldItem => !areSameEntity(oldItem, newItem)))
            {
                addedItems.Add(newItem);
            }
        }

        return (addedItems, removedItems, updatedItems, sameItems);
    }

    /// <summary>Compares each tuple's items with the given function to split them into updated and unchanged pairs.</summary>
    /// <typeparam name="T1">The type of the first element of each pair.</typeparam>
    /// <typeparam name="T2">The type of the second element of each pair.</typeparam>
    /// <param name="existItems">The matched old/new pairs to inspect.</param>
    /// <param name="hasChange">A predicate that determines whether a pair differs.</param>
    /// <returns>
    /// A tuple containing the pairs reported as changed by <paramref name="hasChange"/> (<c>UpdatedItems</c>)
    /// and the pairs reported as unchanged (<c>SameItems</c>).
    /// </returns>
    [MustUseReturnValue]
    public static (List<(T1, T2)> UpdatedItems, List<(T1, T2)> SameItems) DetectUpdates<T1, T2>(
        this List<(T1, T2)> existItems,
        Func<T1, T2, bool> hasChange
    )
    {
        var updatedItems = new List<(T1, T2)>();
        var sameItems = new List<(T1, T2)>();

        foreach (var existItem in existItems)
        {
            if (hasChange(existItem.Item1, existItem.Item2))
            {
                updatedItems.Add(existItem);
            }
            else
            {
                sameItems.Add(existItem);
            }
        }

        return (updatedItems, sameItems);
    }
}
