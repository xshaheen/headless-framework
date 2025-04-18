// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Blobs;

using NextPageFunc = Func<PagedFileListResult, CancellationToken, ValueTask<INextPageResult>>;

public interface INextPageResult
{
    bool Success { get; }

    bool HasMore { get; }

    IReadOnlyCollection<BlobInfo> Blobs { get; }

    NextPageFunc? NextPageFunc { get; }
}

public interface IHasNextPageFunc
{
    NextPageFunc? NextPageFunc { get; }
}

public sealed class PagedFileListResult : IHasNextPageFunc
{
    private static readonly ReadOnlyCollection<BlobInfo> _Empty = new([]);
    public static readonly PagedFileListResult Empty = new(_Empty);

    public bool HasMore { get; private set; }

    public IReadOnlyCollection<BlobInfo> Blobs { get; private set; } = _Empty;

    #region Next Page

    private NextPageFunc? _nextPageFunc;
    NextPageFunc? IHasNextPageFunc.NextPageFunc => _nextPageFunc;

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

    public PagedFileListResult(IReadOnlyCollection<BlobInfo> blobs)
    {
        Blobs = blobs;
        HasMore = false;
        _nextPageFunc = null;
    }

    public PagedFileListResult(IReadOnlyCollection<BlobInfo> blobs, bool hasMore, NextPageFunc nextPageFunc)
    {
        Blobs = blobs;
        HasMore = hasMore;
        _nextPageFunc = nextPageFunc;
    }

    public PagedFileListResult(NextPageFunc nextPageFunc)
    {
        _nextPageFunc = nextPageFunc;
    }

    #endregion
}
