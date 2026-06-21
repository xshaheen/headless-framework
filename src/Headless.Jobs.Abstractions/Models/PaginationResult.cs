// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Models;

/// <summary>
/// A page of items from a larger result set, used by dashboard query endpoints.
/// </summary>
/// <typeparam name="T">The element type of the page.</typeparam>
public class PaginationResult<T>
{
    /// <summary>The items on the current page.</summary>
    public IEnumerable<T> Items { get; set; }

    /// <summary>Total number of matching records across all pages.</summary>
    public int TotalCount { get; set; }

    /// <summary>The current 1-based page number.</summary>
    public int PageNumber { get; set; }

    /// <summary>Maximum number of items per page.</summary>
    public int PageSize { get; set; }

    /// <summary>Total number of pages, computed from <see cref="TotalCount"/> and <see cref="PageSize"/>.</summary>
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary><see langword="true"/> when there is a page before the current one.</summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary><see langword="true"/> when there is a page after the current one.</summary>
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary>1-based index of the first item on this page within the full result set.</summary>
    public int FirstItemIndex => (PageNumber - 1) * PageSize + 1;

    /// <summary>1-based index of the last item on this page within the full result set.</summary>
    public int LastItemIndex => Math.Min(PageNumber * PageSize, TotalCount);

    /// <summary>Initializes an empty pagination result.</summary>
    public PaginationResult()
    {
        Items = new List<T>();
    }

    /// <summary>
    /// Initializes a pagination result with the given items and pagination metadata.
    /// </summary>
    /// <param name="items">The items on this page; defaults to an empty list when <see langword="null"/>.</param>
    /// <param name="totalCount">Total matching records across all pages.</param>
    /// <param name="pageNumber">Current 1-based page number.</param>
    /// <param name="pageSize">Maximum items per page.</param>
    public PaginationResult(IEnumerable<T> items, int totalCount, int pageNumber, int pageSize)
    {
        Items = items ?? new List<T>();
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }
}
