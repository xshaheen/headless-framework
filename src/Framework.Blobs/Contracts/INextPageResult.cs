using System.Collections.ObjectModel;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Blobs;

public interface INextPageResult
{
    public bool Success { get; }

    public bool HasMore { get; }

    public IReadOnlyCollection<BlobSpecification> Blobs { get; }

    public Func<PagedFileListResult, Task<INextPageResult>>? NextPageFunc { get; }
}

public interface IHasNextPageFunc
{
    Func<PagedFileListResult, Task<INextPageResult>>? NextPageFunc { get; }
}

public sealed class PagedFileListResult : IHasNextPageFunc
{
    private static readonly ReadOnlyCollection<BlobSpecification> _Empty = new([]);
    public static readonly PagedFileListResult Empty = new(_Empty);

    public bool HasMore { get; private set; }

    public IReadOnlyCollection<BlobSpecification> Blobs { get; private set; } = _Empty;

    #region Next Page

    private Func<PagedFileListResult, Task<INextPageResult>>? _nextPageFunc;
    Func<PagedFileListResult, Task<INextPageResult>>? IHasNextPageFunc.NextPageFunc => _nextPageFunc;

    public async Task<bool> NextPageAsync()
    {
        IHasNextPageFunc func = this;

        if (func.NextPageFunc is null)
        {
            return false;
        }

        var result = await func.NextPageFunc(this);

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

    public PagedFileListResult(IReadOnlyCollection<BlobSpecification> blobs)
    {
        Blobs = blobs;
        HasMore = false;
        _nextPageFunc = null;
    }

    public PagedFileListResult(
        IReadOnlyCollection<BlobSpecification> blobs,
        bool hasMore,
        Func<PagedFileListResult, Task<INextPageResult>> nextPageFunc
    )
    {
        Blobs = blobs;
        HasMore = hasMore;
        _nextPageFunc = nextPageFunc;
    }

    public PagedFileListResult(Func<PagedFileListResult, Task<INextPageResult>> nextPageFunc)
    {
        _nextPageFunc = nextPageFunc;
    }

    #endregion
}
