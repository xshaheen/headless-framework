// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

[PublicAPI]
public static class HeadlessListExtensions
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
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is less than 0 or greater than <see cref="ICollection{T}.Count"/>.
    /// </exception>
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
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> or <paramref name="count"/> is less than 0, or the range they specify falls outside the list.
    /// </exception>
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

    /// <summary>Inserts an item at the beginning of the list.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The list to insert into.</param>
    /// <param name="item">The item to insert at index 0.</param>
    public static void AddFirst<T>(this IList<T> source, T item)
    {
        source.Insert(0, item);
    }

    /// <summary>Adds an item to the end of the list.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The list to add to.</param>
    /// <param name="item">The item to append.</param>
    public static void AddLast<T>(this IList<T> source, T item)
    {
        source.Insert(source.Count, item);
    }

    /// <summary>
    /// Inserts <paramref name="item"/> immediately after the first occurrence of <paramref name="existingItem"/>.
    /// If <paramref name="existingItem"/> is not found, <paramref name="item"/> is inserted at the beginning of the list.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The list to insert into.</param>
    /// <param name="existingItem">The item to search for as the insertion anchor.</param>
    /// <param name="item">The item to insert.</param>
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

    /// <summary>
    /// Inserts <paramref name="item"/> immediately after the first element matching <paramref name="selector"/>.
    /// If no element matches, <paramref name="item"/> is inserted at the beginning of the list.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The list to insert into.</param>
    /// <param name="selector">A predicate that identifies the insertion anchor.</param>
    /// <param name="item">The item to insert.</param>
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

    /// <summary>
    /// Inserts <paramref name="item"/> immediately before the first occurrence of <paramref name="existingItem"/>.
    /// If <paramref name="existingItem"/> is not found, <paramref name="item"/> is added to the end of the list.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The list to insert into.</param>
    /// <param name="existingItem">The item to search for as the insertion anchor.</param>
    /// <param name="item">The item to insert.</param>
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

    /// <summary>
    /// Inserts <paramref name="item"/> immediately before the first element matching <paramref name="selector"/>.
    /// If no element matches, <paramref name="item"/> is added to the end of the list.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The list to insert into.</param>
    /// <param name="selector">A predicate that identifies the insertion anchor.</param>
    /// <param name="item">The item to insert.</param>
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

    /// <summary>Replaces every element matching <paramref name="selector"/> with <paramref name="item"/>.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The list to modify in place.</param>
    /// <param name="selector">A predicate that identifies the elements to replace.</param>
    /// <param name="item">The replacement value.</param>
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

    /// <summary>
    /// Replaces every element matching <paramref name="selector"/> with the value produced by <paramref name="itemFactory"/>
    /// for that element.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The list to modify in place.</param>
    /// <param name="selector">A predicate that identifies the elements to replace.</param>
    /// <param name="itemFactory">A factory that maps a matched element to its replacement.</param>
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

    /// <summary>Replaces the first element matching <paramref name="selector"/> with <paramref name="item"/>.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The list to modify in place.</param>
    /// <param name="selector">A predicate that identifies the element to replace.</param>
    /// <param name="item">The replacement value.</param>
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

    /// <summary>
    /// Replaces the first element matching <paramref name="selector"/> with the value produced by
    /// <paramref name="itemFactory"/> for that element.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The list to modify in place.</param>
    /// <param name="selector">A predicate that identifies the element to replace.</param>
    /// <param name="itemFactory">A factory that maps the matched element to its replacement.</param>
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

    /// <summary>
    /// Replaces the first element equal to <paramref name="item"/> (per <see cref="EqualityComparer{T}.Default"/>) with
    /// <paramref name="replaceWith"/>.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The list to modify in place.</param>
    /// <param name="item">The value to search for.</param>
    /// <param name="replaceWith">The replacement value.</param>
    public static void ReplaceFirst<T>(this IList<T> source, T item, T replaceWith)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(source[i], item))
            {
                source[i] = replaceWith;

                return;
            }
        }
    }

    /// <summary>
    /// Moves the first element matching <paramref name="selector"/> to <paramref name="targetIndex"/>.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The list to reorder.</param>
    /// <param name="selector">A predicate that identifies the element to move.</param>
    /// <param name="targetIndex">The zero-based index the matched element should be moved to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="source"/> is empty (so no valid target index exists).</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="targetIndex"/> is outside the range <c>[0, source.Count - 1]</c>, or when no element
    /// matches <paramref name="selector"/>.
    /// </exception>
    public static void MoveItem<T>(this List<T> source, Predicate<T> selector, [NonNegativeValue] int targetIndex)
    {
        Argument.IsNotNull(source);
        Argument.IsInclusiveBetween(targetIndex, 0, source.Count - 1);

        var currentIndex = source.FindIndex(0, selector);

        Argument.IsInRangeFor(
            currentIndex,
            source,
            message: "No element in the list matches the given selector.",
            paramName: nameof(selector)
        );

        if (currentIndex == targetIndex)
        {
            return;
        }

        var item = source[currentIndex];
        source.RemoveAt(currentIndex);
        source.Insert(targetIndex, item);
    }

    /// <summary>Returns the index of the first element matching <paramref name="selector"/>, or <c>-1</c> if none match.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The list to search.</param>
    /// <param name="selector">A predicate that identifies the element to locate.</param>
    /// <returns>The zero-based index of the first matching element, or <c>-1</c> if no element matches.</returns>
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

    /// <summary>
    /// Returns the first element matching <paramref name="selector"/>; if none matches, creates an element with
    /// <paramref name="factory"/>, appends it to the list, and returns it.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The list to search and possibly append to.</param>
    /// <param name="selector">A predicate that identifies an existing matching element.</param>
    /// <param name="factory">A factory that creates a new element when no match is found.</param>
    /// <returns>The existing matching element, or the newly created element added to the list.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    [MustUseReturnValue]
    public static T GetOrAdd<T>(this IList<T> source, Func<T, bool> selector, Func<T> factory)
    {
        Argument.IsNotNull(source);

        // Scan by index instead of FirstOrDefault + "is not null": for value-type T, the latter never
        // detects a default(T) match, so the element would always be re-created and appended.
        for (var i = 0; i < source.Count; i++)
        {
            if (selector(source[i]))
            {
                return source[i];
            }
        }

        var item = factory();
        source.Add(item);

        return item;
    }

    /// <summary>
    /// Get a <see cref="ReadOnlySpan{T}"/> view over a <see cref="List{T}"/>'s data.
    /// Items should not be added or removed from the <see cref="List{T}"/> while the <see cref="ReadOnlySpan{T}"/> is in use.
    /// </summary>
    /// <param name="list">The list to get the data view over.</param>
    /// <typeparam name="T">The type of the elements in the list.</typeparam>
    /// <returns>A <see cref="ReadOnlySpan{T}"/> over <paramref name="list"/>'s internal buffer.</returns>
    [MustUseReturnValue]
    public static ReadOnlySpan<T> AsReadOnlySpan<T>(this List<T> list)
    {
        return CollectionsMarshal.AsSpan(list);
    }
}
