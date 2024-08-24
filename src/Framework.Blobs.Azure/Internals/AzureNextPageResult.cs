namespace Framework.Blobs.Azure.Internals;

public sealed class AzureNextPageResult : INextPageResult
{
    public required bool Success { get; init; }

    public required bool HasMore { get; init; }

    public required IReadOnlyCollection<BlobSpecification> Blobs { get; init; }

    public required IReadOnlyCollection<BlobSpecification> ExtraLoadedBlobs { get; init; }

    public required string? ContinuationToken { get; init; }

    public required Func<AzureNextPageResult, Task<AzureNextPageResult>>? AzureNextPageFunc { get; init; }

    public Func<PagedFileListResult, Task<INextPageResult>>? NextPageFunc =>
        AzureNextPageFunc is null ? null : async result => await AzureNextPageFunc.Invoke(this);
}
