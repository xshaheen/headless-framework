// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

[PublicAPI]
public static class CollectionChangeDetectorExtensions
{
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

    /// <summary>Compare each tuple items with the given function to detect the updated items.</summary>
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
