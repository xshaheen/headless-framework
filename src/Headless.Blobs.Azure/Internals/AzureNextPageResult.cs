// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;

namespace Headless.Blobs.Azure.Internals;

public sealed class AzureNextPageResult : INextPageResult
{
    public required bool Success { get; init; }

    public required bool HasMore { get; init; }

    public required IReadOnlyCollection<BlobInfo> Blobs { get; init; }

    /// <summary>
    /// The extra blob fetched by the +1 approach, to be returned on the next page.
    /// </summary>
    internal BlobInfo? ExtraBlob { get; init; }

    /// <summary>
    /// Azure SDK continuation token for fetching more blobs from the service.
    /// </summary>
    internal string? ContinuationToken { get; init; }

    public required Func<
        AzureNextPageResult,
        CancellationToken,
        Task<AzureNextPageResult>
    >? AzureNextPageFunc { get; init; }

    public Func<PagedFileListResult, CancellationToken, ValueTask<INextPageResult>>? NextPageFunc =>
        AzureNextPageFunc is null ? null : async (_, token) => await AzureNextPageFunc.Invoke(this, token).AnyContext();
}
