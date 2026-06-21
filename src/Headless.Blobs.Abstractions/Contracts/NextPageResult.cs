// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>
/// Concrete implementation of <see cref="INextPageResult"/> produced by provider paging helpers.
/// </summary>
public sealed class NextPageResult : INextPageResult
{
    /// <inheritdoc />
    public required bool Success { get; init; }

    /// <inheritdoc />
    public required bool HasMore { get; init; }

    /// <inheritdoc />
    public required IReadOnlyCollection<BlobInfo> Blobs { get; init; }

    /// <inheritdoc />
    public Func<PagedFileListResult, CancellationToken, ValueTask<INextPageResult>>? NextPageFunc { get; init; }
}
