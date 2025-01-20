// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Blobs.Azure.Internals;

public sealed class AzureNextPageResult : INextPageResult
{
    public required bool Success { get; init; }

    public required bool HasMore { get; init; }

    public required IReadOnlyCollection<BlobInfo> Blobs { get; init; }

    public required IReadOnlyCollection<BlobInfo> ExtraLoadedBlobs { get; init; }

    public required string? ContinuationToken { get; init; }

    public required Func<AzureNextPageResult, CancellationToken, Task<AzureNextPageResult>>? AzureNextPageFunc { get; init; }

    public Func<PagedFileListResult, CancellationToken, ValueTask<INextPageResult>>? NextPageFunc =>
        AzureNextPageFunc is null ? null : async (_, token) => await AzureNextPageFunc.Invoke(this, token);
}
