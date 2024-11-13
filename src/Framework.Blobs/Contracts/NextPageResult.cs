// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Blobs;

public sealed class NextPageResult : INextPageResult
{
    public required bool Success { get; init; }

    public required bool HasMore { get; init; }

    public required IReadOnlyCollection<BlobSpecification> Blobs { get; init; }

    public Func<PagedFileListResult, ValueTask<INextPageResult>>? NextPageFunc { get; init; }
}
