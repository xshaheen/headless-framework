// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Framework.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

[PublicAPI]
public static class ListExtensions
{
    /// <summary>Inserts the elements of a collection into the <see cref="IList{T}"/> at the specified index.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source" />.</typeparam>
    /// <param name="source">The list to which new elements will be inserted.</param>
    /// <param name="index">The zero-based index at which the new elements should be inserted.</param>
    /// <param name="items">
    /// The collection whose elements should be inserted into the <see cref="IList{T}"/>. The collection itself cannot
    /// be a null reference (<c>Nothing</c> in Visual Basic), but it can contain elements that are a null
    /// reference (<c>Nothing</c> in Visual Basic), if type <typeparamref name="T"/> is a reference type.
    /// </param>
    public static void InsertRange<T>(this IList<T> source, [NonNegativeValue] int index, IEnumerable<T> items)
    {
        if (source is List<T> concreteList)
        {
            concreteList.InsertRange(index, items);

            return;
        }

        var currentIndex = index;

        foreach (var item in items)
        {
            source.Insert(currentIndex++, item);
        }
    }

    /// <summary>Removes a range of elements from the <see cref="IList{T}"/>.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="list" />.</typeparam>
    /// <param name="list">The list to remove range from.</param>
    /// <param name="index">The zero-based starting index of the range of elements to remove.</param>
    /// <param name="count">The number of elements to remove.</param>
    public static void RemoveRange<T>(this IList<T> list, [NonNegativeValue] int index, [NonNegativeValue] int count)
    {
        if (list is List<T> concreteList)
        {
            concreteList.RemoveRange(index, count);

            return;
        }

        for (var offset = count - 1; offset >= 0; offset--)
        {
            list.RemoveAt(index + offset);
        }
    }

    public static void AddFirst<T>(this IList<T> source, T item)
    {
        source.Insert(0, item);
    }

    public static void AddLast<T>(this IList<T> source, T item)
    {
        source.Insert(source.Count, item);
    }

    public static void InsertAfter<T>(this IList<T> source, T existingItem, T item)
    {
        var index = source.IndexOf(existingItem);

        if (index < 0)
        {
            source.AddFirst(item);

            return;
        }

        source.Insert(index + 1, item);
    }

    public static void InsertAfter<T>(this IList<T> source, Predicate<T> selector, T item)
    {
        var index = source.FindIndex(selector);

        if (index < 0)
        {
            source.AddFirst(item);

            return;
        }

        source.Insert(index + 1, item);
    }

    public static void InsertBefore<T>(this IList<T> source, T existingItem, T item)
    {
        var index = source.IndexOf(existingItem);

        if (index < 0)
        {
            source.AddLast(item);

            return;
        }

        source.Insert(index, item);
    }

    public static void InsertBefore<T>(this IList<T> source, Predicate<T> selector, T item)
    {
        var index = source.FindIndex(selector);

        if (index < 0)
        {
            source.AddLast(item);

            return;
        }

        source.Insert(index, item);
    }

    public static void ReplaceWhile<T>(this IList<T> source, Predicate<T> selector, T item)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (selector(source[i]))
            {
                source[i] = item;
            }
        }
    }

    public static void ReplaceWhile<T>(this IList<T> source, Predicate<T> selector, Func<T, T> itemFactory)
    {
        for (var i = 0; i < source.Count; i++)
        {
            var item = source[i];

            if (selector(item))
            {
                source[i] = itemFactory(item);
            }
        }
    }

    public static void ReplaceFirst<T>(this IList<T> source, Predicate<T> selector, T item)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (selector(source[i]))
            {
                source[i] = item;

                return;
            }
        }
    }

    public static void ReplaceFirst<T>(this IList<T> source, Predicate<T> selector, Func<T, T> itemFactory)
    {
        for (var i = 0; i < source.Count; i++)
        {
            var item = source[i];

            if (selector(item))
            {
                source[i] = itemFactory(item);

                return;
            }
        }
    }

    public static void ReplaceFirst<T>(this IList<T> source, T item, T replaceWith)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (Comparer<T>.Default.Compare(source[i], item) == 0)
            {
                source[i] = replaceWith;

                return;
            }
        }
    }

    public static void MoveItem<T>(this List<T> source, Predicate<T> selector, [NonNegativeValue] int targetIndex)
    {
        Argument.IsNotNull(source);
        Argument.IsInclusiveBetween(targetIndex, 0, source.Count - 1);

        var currentIndex = source.FindIndex(0, selector);

        if (currentIndex == targetIndex)
        {
            return;
        }

        var item = source[currentIndex];
        source.RemoveAt(currentIndex);
        source.Insert(targetIndex, item);
    }

    [MustUseReturnValue]
    public static int FindIndex<T>(this IList<T> source, Predicate<T> selector)
    {
        for (var i = 0; i < source.Count; ++i)
        {
            if (selector(source[i]))
            {
                return i;
            }
        }

        return -1;
    }

    [MustUseReturnValue]
    public static T GetOrAdd<T>(this IList<T> source, Func<T, bool> selector, Func<T> factory)
    {
        Argument.IsNotNull(source);

        var item = source.FirstOrDefault(selector);

        if (item is not null)
        {
            return item;
        }

        item = factory();
        source.Add(item);

        return item;
    }

    /// <summary>
    /// Get a <see cref="ReadOnlySpan{T}"/> view over a <see cref="List{T}"/>'s data.
    /// Items should not be added or removed from the <see cref="List{T}"/> while the <see cref="ReadOnlySpan{T}"/> is in use.
    /// </summary>
    /// <param name="list">The list to get the data view over.</param>
    /// <typeparam name="T">The type of the elements in the list.</typeparam>
    [MustUseReturnValue]
    public static ReadOnlySpan<T> AsReadOnlySpan<T>(this List<T> list)
    {
        return CollectionsMarshal.AsSpan(list);
    }

    /// <inheritdoc cref="CollectionsMarshal.AsSpan{T}"/>
    [MustUseReturnValue]
    public static ReadOnlySpan<T> AsSpan<T>(this List<T> list)
    {
        return CollectionsMarshal.AsSpan(list);
    }
}
