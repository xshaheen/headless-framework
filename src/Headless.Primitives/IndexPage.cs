// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>A single page of items returned by zero-based offset (index/size) pagination.</summary>
/// <typeparam name="T">The item type.</typeparam>
[PublicAPI]
public sealed class IndexPage<T>
{
    /// <summary>Initializes a new page and computes <see cref="TotalPages"/> from <paramref name="totalItems"/> and <paramref name="size"/>.</summary>
    /// <param name="items">The items on this page.</param>
    /// <param name="index">The zero-based index of this page.</param>
    /// <param name="size">The page size.</param>
    /// <param name="totalItems">The total number of items across all pages.</param>
    public IndexPage(IReadOnlyList<T> items, int index, int size, int totalItems)
    {
        Items = items;
        Index = index;
        Size = size;
        TotalItems = totalItems;
        TotalPages = TotalItems == 0 || Size == 0 ? 0 : (int)Math.Ceiling(TotalItems / (decimal)Size);
    }

    /// <summary>The items on this page.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>The zero-based index of this page.</summary>
    public int Index { get; }

    /// <summary>The page size.</summary>
    public int Size { get; }

    /// <summary>The total number of items across all pages.</summary>
    public int TotalItems { get; }

    /// <summary>The total number of pages, or <c>0</c> when there are no items or the page size is zero.</summary>
    public int TotalPages { get; }

    /// <summary><see langword="true"/> when a previous page exists (the page index is greater than zero).</summary>
    public bool HasPrevious => Index > 0;

    /// <summary><see langword="true"/> when a further page exists (the page index is before the last page).</summary>
    public bool HasNext => Index < TotalPages - 1;

    /// <summary>Projects each item to a new form, preserving the <see cref="Index"/>, <see cref="Size"/>, and <see cref="TotalItems"/>.</summary>
    /// <typeparam name="TOutput">The projected item type.</typeparam>
    /// <param name="map">The projection applied to each item.</param>
    /// <returns>A new <see cref="IndexPage{TOutput}"/> with the projected items.</returns>
    public IndexPage<TOutput> Select<TOutput>(Func<T, TOutput> map)
    {
        return new(Items.Select(map).ToList(), Index, Size, TotalItems);
    }

    /// <summary>Filters the items, preserving the <see cref="Index"/>, <see cref="Size"/>, and <see cref="TotalItems"/>.</summary>
    /// <param name="predicate">The filter applied to each item.</param>
    /// <returns>A new <see cref="IndexPage{T}"/> containing only the matching items.</returns>
    public IndexPage<T> Where(Func<T, bool> predicate)
    {
        return new(Items.Where(predicate).ToList(), Index, Size, TotalItems);
    }
}
