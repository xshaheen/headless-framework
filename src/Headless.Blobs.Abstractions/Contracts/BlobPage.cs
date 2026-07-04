// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>
/// One page of blobs returned by <see cref="IBlobStorage.ListAsync"/>, paired with an opaque continuation token.
/// </summary>
/// <remarks>
/// A <see langword="null"/> <paramref name="ContinuationToken"/> marks the last page. The token is provider-specific
/// (S3/Azure native tokens, a filesystem/SFTP start-after-key, a Redis <c>HSCAN</c> cursor) and must be treated as
/// opaque — callers round-trip it back into a new <see cref="BlobQuery"/> and must not parse, compare, or persist it
/// across provider changes.
/// </remarks>
/// <param name="Items">The blobs on this page.</param>
/// <param name="ContinuationToken">Opaque token to fetch the next page, or <see langword="null"/> when this is the last page.</param>
[PublicAPI]
public sealed record BlobPage(IReadOnlyList<BlobInfo> Items, string? ContinuationToken)
{
    /// <summary>A completed, empty page with no items and no continuation token.</summary>
    public static BlobPage Empty { get; } = new([], ContinuationToken: null);
}
