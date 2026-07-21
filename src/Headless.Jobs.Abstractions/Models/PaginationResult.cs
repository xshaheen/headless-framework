// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Models;

/// <summary>
/// A page of items from a larger result set, used by dashboard query endpoints.
/// </summary>
/// <typeparam name="T">The element type of the page.</typeparam>
[PublicAPI]
public sealed class PaginationResult<T>
{
    /// <summary>The items on the current page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Total number of matching records across all pages.</summary>
    public required int TotalCount { get; init; }

    /// <summary>The current 1-based page number.</summary>
    public required int PageNumber { get; init; }

    /// <summary>Maximum number of items per page.</summary>
    public required int PageSize { get; init; }

    /// <summary>Total number of pages, computed from <see cref="TotalCount"/> and <see cref="PageSize"/>.</summary>
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary><see langword="true"/> when there is a page before the current one.</summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary><see langword="true"/> when there is a page after the current one.</summary>
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary>1-based index of the first item on this page within the full result set.</summary>
    public int FirstItemIndex => ((PageNumber - 1) * PageSize) + 1;

    /// <summary>1-based index of the last item on this page within the full result set.</summary>
    public int LastItemIndex => Math.Min(PageNumber * PageSize, TotalCount);
}
