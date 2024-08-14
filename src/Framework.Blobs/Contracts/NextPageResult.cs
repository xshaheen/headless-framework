using System.Collections.ObjectModel;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Blobs;

public sealed class NextPageResult
{
    public required bool Success { get; set; }

    public required bool HasMore { get; set; }

    public required IReadOnlyCollection<BlobSpecification> Files { get; set; }

    public Func<PagedFileListResult, Task<NextPageResult>>? NextPageFunc { get; set; }
}

public interface IHasNextPageFunc
{
    Func<PagedFileListResult, Task<NextPageResult>>? NextPageFunc { get; set; }
}

public sealed class PagedFileListResult : IHasNextPageFunc
{
    private static readonly ReadOnlyCollection<BlobSpecification> _Empty = new([]);

    private Dictionary<string, object> Data { get; } = [];

    public static readonly PagedFileListResult Empty = new(_Empty);

    public PagedFileListResult(IReadOnlyCollection<BlobSpecification> files)
    {
        Files = files;
        HasMore = false;
        ((IHasNextPageFunc)this).NextPageFunc = null;
    }

    public PagedFileListResult(
        IReadOnlyCollection<BlobSpecification> files,
        bool hasMore,
        Func<PagedFileListResult, Task<NextPageResult>> nextPageFunc
    )
    {
        Files = files;
        HasMore = hasMore;
        ((IHasNextPageFunc)this).NextPageFunc = nextPageFunc;
    }

    public PagedFileListResult(Func<PagedFileListResult, Task<NextPageResult>> nextPageFunc)
    {
        ((IHasNextPageFunc)this).NextPageFunc = nextPageFunc;
    }

    public IReadOnlyCollection<BlobSpecification> Files { get; private set; } = _Empty;

    public bool HasMore { get; private set; }

    Func<PagedFileListResult, Task<NextPageResult>>? IHasNextPageFunc.NextPageFunc { get; set; }

    public async Task<bool> NextPageAsync()
    {
        var func = (IHasNextPageFunc)this;

        if (func.NextPageFunc == null)
        {
            return false;
        }

        var result = await func.NextPageFunc(this);

        if (result.Success)
        {
            Files = result.Files;
            HasMore = result.HasMore;
            func.NextPageFunc = result.NextPageFunc;
        }
        else
        {
            Files = _Empty;
            HasMore = false;
            func.NextPageFunc = null;
        }

        return result.Success;
    }
}
