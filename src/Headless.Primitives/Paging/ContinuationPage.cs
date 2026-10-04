// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>A single page of items returned by token-based (continuation/cursor) pagination.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="items">The items on this page.</param>
/// <param name="size">The requested page size.</param>
/// <param name="continuationToken">The token used to fetch the next page, or <see langword="null"/> when this is the last page.</param>
[PublicAPI]
public sealed class ContinuationPage<T>(IReadOnlyList<T> items, int size, string? continuationToken)
{
    /// <summary>The items on this page.</summary>
    public IReadOnlyList<T> Items { get; } = items;

    /// <summary>The requested page size.</summary>
    public int Size { get; } = size;

    /// <summary>The token used to fetch the next page, or <see langword="null"/> when this is the last page.</summary>
    public string? ContinuationToken { get; } = continuationToken;

    /// <summary><see langword="true"/> when a further page is available (the <see cref="ContinuationToken"/> is non-<see langword="null"/>).</summary>
    public bool HasNext => ContinuationToken is not null;

    /// <summary>Projects each item to a new form, preserving the page <see cref="Size"/> and <see cref="ContinuationToken"/>.</summary>
    /// <typeparam name="TOutput">The projected item type.</typeparam>
    /// <param name="map">The projection applied to each item.</param>
    /// <returns>A new <see cref="ContinuationPage{TOutput}"/> with the projected items.</returns>
    public ContinuationPage<TOutput> Select<TOutput>(Func<T, TOutput> map)
    {
        return new(Items.Select(map).ToList(), Size, ContinuationToken);
    }

    /// <summary>Filters the items, preserving the page <see cref="Size"/> and <see cref="ContinuationToken"/>.</summary>
    /// <param name="predicate">The filter applied to each item.</param>
    /// <returns>A new <see cref="ContinuationPage{T}"/> containing only the matching items.</returns>
    public ContinuationPage<T> Where(Func<T, bool> predicate)
    {
        return new(Items.Where(predicate).ToList(), Size, ContinuationToken);
    }
}
