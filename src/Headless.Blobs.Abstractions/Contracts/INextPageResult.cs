// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

using NextPageFunc = Func<PagedFileListResult, CancellationToken, ValueTask<INextPageResult>>;

/// <summary>
/// Outcome of a single page fetch, carrying the page's blobs and a continuation delegate for the next page.
/// </summary>
/// <remarks>
/// This interface is the internal exchange type between a provider's paging implementation and
/// <see cref="PagedFileListResult"/>. Consumer code works with <see cref="PagedFileListResult"/> directly.
/// </remarks>
public interface INextPageResult
{
    /// <summary><see langword="true"/> when the page fetch succeeded and <see cref="Blobs"/> is valid.</summary>
    bool Success { get; }

    /// <summary><see langword="true"/> when at least one more page is available after this one.</summary>
    bool HasMore { get; }

    /// <summary>The blobs returned in this page.</summary>
    IReadOnlyCollection<BlobInfo> Blobs { get; }

    /// <summary>
    /// Delegate that retrieves the next page, or <see langword="null"/> when there are no further pages.
    /// </summary>
    NextPageFunc? NextPageFunc { get; }
}

/// <summary>Exposes the internal next-page delegate to <see cref="PagedFileListResult"/> without making it public API.</summary>
public interface IHasNextPageFunc
{
    NextPageFunc? NextPageFunc { get; }
}

/// <summary>
/// Stateful cursor for iterating blobs page-by-page. Returned by <see cref="IBlobStorage.GetPagedListAsync"/>.
/// </summary>
/// <remarks>
/// After receiving the first page, advance through subsequent pages by calling <see cref="NextPageAsync"/> while
/// <see cref="HasMore"/> is <see langword="true"/>. Each call mutates the same instance in place.
/// </remarks>
public sealed class PagedFileListResult : IHasNextPageFunc
{
    private static readonly ReadOnlyCollection<BlobInfo> _Empty = new([]);

    /// <summary>A completed, empty result with no blobs and no further pages.</summary>
    public static readonly PagedFileListResult Empty = new(_Empty);

    /// <summary><see langword="true"/> when at least one more page can be fetched via <see cref="NextPageAsync"/>.</summary>
    public bool HasMore { get; private set; }

    /// <summary>The blobs on the current page.</summary>
    public IReadOnlyCollection<BlobInfo> Blobs { get; private set; } = _Empty;

    #region Next Page

    private NextPageFunc? _nextPageFunc;
    NextPageFunc? IHasNextPageFunc.NextPageFunc => _nextPageFunc;

    /// <summary>
    /// Fetches the next page of blobs and updates <see cref="Blobs"/> and <see cref="HasMore"/> in place.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the page was fetched successfully; <see langword="false"/> if there are no more
    /// pages or the page fetch failed, in which case <see cref="Blobs"/> is reset to an empty collection.
    /// </returns>
    public async Task<bool> NextPageAsync(CancellationToken cancellationToken = default)
    {
        IHasNextPageFunc func = this;

        if (func.NextPageFunc is null)
        {
            return false;
        }

        var result = await func.NextPageFunc(this, cancellationToken);

        if (result.Success)
        {
            Blobs = result.Blobs;
            HasMore = result.HasMore;
            _nextPageFunc = result.NextPageFunc;
        }
        else
        {
            Blobs = _Empty;
            HasMore = false;
            _nextPageFunc = null;
        }

        return result.Success;
    }

    #endregion

    #region Constructors

    /// <summary>Creates a terminal page (no further pages) containing <paramref name="blobs"/>.</summary>
    public PagedFileListResult(IReadOnlyCollection<BlobInfo> blobs)
    {
        Blobs = blobs;
        HasMore = false;
        _nextPageFunc = null;
    }

    /// <summary>
    /// Creates a page containing <paramref name="blobs"/> with an explicit continuation delegate.
    /// </summary>
    /// <param name="blobs">Blobs on this page.</param>
    /// <param name="hasMore">Whether more pages exist.</param>
    /// <param name="nextPageFunc">Delegate to fetch the next page.</param>
    public PagedFileListResult(IReadOnlyCollection<BlobInfo> blobs, bool hasMore, NextPageFunc nextPageFunc)
    {
        Blobs = blobs;
        HasMore = hasMore;
        _nextPageFunc = nextPageFunc;
    }

    /// <summary>
    /// Creates an uninitialized cursor whose first page is loaded by calling <see cref="NextPageAsync"/> with the
    /// supplied delegate. Used by providers that load the first page lazily via <see cref="NextPageAsync"/>.
    /// </summary>
    public PagedFileListResult(NextPageFunc nextPageFunc)
    {
        _nextPageFunc = nextPageFunc;
    }

    #endregion
}
