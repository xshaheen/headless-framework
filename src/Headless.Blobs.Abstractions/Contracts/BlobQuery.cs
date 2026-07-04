// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.Internals;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>
/// Describes a single page request against a blob container: which container to enumerate, an optional server-pushed
/// key <see cref="Prefix"/>, the page size, and an opaque <see cref="ContinuationToken"/> from a previous
/// <see cref="BlobPage"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Container"/> and <see cref="Prefix"/> are validated for path security at construction through the same
/// seam as <see cref="BlobLocation"/> (<see cref="PathValidation.ValidatePathSegment"/>), so a traversal prefix
/// (<c>../</c>, absolute, or control characters) can never reach directory enumeration on filesystem-like backends.
/// </para>
/// <para>
/// The <see cref="Prefix"/> is the only filter pushed down to the backend. Glob filtering (<c>*</c>/<c>?</c>) is a
/// client-side concern layered over <see cref="IBlobStorage.ListAsync"/> (see <c>GetBlobsAsync(query, glob)</c>),
/// not a server-side capability of this query.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record BlobQuery
{
    /// <summary>Creates a query for one page of blobs in <paramref name="container"/>.</summary>
    /// <param name="container">The top-level container to enumerate (bucket/container/root). Must not be null, empty, or whitespace.</param>
    /// <param name="prefix">Optional server-pushed key prefix. <see langword="null"/> or empty lists every blob in the container.</param>
    /// <param name="pageSize">Maximum number of blobs returned per page. Must be positive.</param>
    /// <param name="continuationToken">Opaque token from a previous <see cref="BlobPage"/>, or <see langword="null"/> to start from the first page.</param>
    /// <param name="includeMetadata">
    /// When <see langword="true"/>, listings populate <see cref="BlobInfo.Metadata"/> per blob; otherwise (the default)
    /// listings omit metadata. See <see cref="IncludeMetadata"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="container"/> is empty/whitespace, or when <paramref name="container"/> or a non-empty
    /// <paramref name="prefix"/> contains a path-traversal sequence, is absolute, or contains control characters, or
    /// when <paramref name="prefix"/> contains a segment ending in the reserved sidecar-metadata suffix.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pageSize"/> is not positive.</exception>
    public BlobQuery(
        string container,
        string? prefix = null,
        int pageSize = 100,
        string? continuationToken = null,
        bool includeMetadata = false
    )
    {
        Container = Argument.IsNotNullOrWhiteSpace(container);
        PathValidation.ValidatePathSegment(container);

        if (!string.IsNullOrEmpty(prefix))
        {
            PathValidation.ValidatePathSegment(prefix);

            // Reject a sidecar-suffix prefix at construction, mirroring BlobLocation, so rejection is symmetric across
            // the two addressing types (the resolve seam re-checks it too, but failing here is clearer).
            if (BlobStorageHelpers.HasSidecarSegment(prefix))
            {
                throw new ArgumentException(
                    $"Blob query prefix segments ending in the reserved sidecar suffix '{BlobStorageHelpers.SidecarSuffix}' are not allowed.",
                    nameof(prefix)
                );
            }
        }

        Argument.IsPositive(pageSize);
        PageSize = pageSize;

        Prefix = prefix;
        ContinuationToken = continuationToken;
        IncludeMetadata = includeMetadata;
    }

    /// <summary>The top-level container (bucket/container/root) to enumerate.</summary>
    public string Container { get; }

    /// <summary>Optional server-pushed key prefix; <see langword="null"/> lists every blob in the container.</summary>
    public string? Prefix { get; }

    /// <summary>Maximum number of blobs returned per page. Always positive.</summary>
    public int PageSize { get; }

    /// <summary>Opaque continuation token carried over from a previous <see cref="BlobPage"/>, or <see langword="null"/> for the first page.</summary>
    public string? ContinuationToken { get; }

    /// <summary>
    /// When <see langword="true"/>, listings populate <see cref="BlobInfo.Metadata"/> for each returned blob; otherwise
    /// (the default) listings return <see langword="null"/> metadata uniformly across providers. Populating metadata in
    /// a listing may cost an extra per-object round-trip on some backends (an S3/SFTP HEAD or a filesystem sidecar read
    /// per blob), so it is opt-in. Use <see cref="IBlobStorage.GetBlobInfoAsync"/> for authoritative single-blob metadata.
    /// </summary>
    public bool IncludeMetadata { get; }
}
