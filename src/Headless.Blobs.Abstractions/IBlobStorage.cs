// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Blobs;

/// <summary>
/// Provider-agnostic contract for blob storage operations: upload, download, delete, move, copy, list, and existence
/// checks.
/// </summary>
/// <remarks>
/// <para>
/// Every operation identifies a blob by a single validated <see cref="BlobLocation"/> (top-level container plus a
/// container-relative object key). The location's constructor validates path security (traversal, control characters,
/// absolute paths, any segment ending in the reserved sidecar suffix) once, so no operation re-implements that guard.
/// Provider-specific naming rules are applied by the provider's resolve step via the registered
/// <see cref="IBlobNamingNormalizer"/>.
/// </para>
/// <para>
/// Container/bucket lifecycle (create/exists/delete) is not part of this data-plane contract — it lives on the opt-in
/// <see cref="IBlobContainerManager"/> capability. <see cref="UploadAsync"/> does not create a missing top-level
/// container; that is an error. Filesystem-like providers still create the intermediate path directories inherent to
/// writing a blob.
/// </para>
/// </remarks>
[PublicAPI]
public interface IBlobStorage : IAsyncDisposable
{
    #region Upload

    /// <summary>Uploads <paramref name="content"/> as the blob identified by <paramref name="location"/>.</summary>
    /// <param name="location">The blob to write.</param>
    /// <param name="content">
    /// The content to upload. Seekable streams are rewound to position 0 before upload; handling of non-seekable
    /// streams is provider-specific (some buffer to memory, some stream through) and is not a uniform promise.
    /// </param>
    /// <param name="metadata">Optional key/value metadata to store alongside the blob (non-null values).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The top-level container must already exist; a missing container/bucket is an error, not auto-created. Use
    /// <see cref="IBlobContainerManager.EnsureContainerAsync"/> (or out-of-band provisioning) first. Exception: Redis
    /// has no physical container to provision — the backing hash is created implicitly on first write, so an upload
    /// against a never-ensured container succeeds there.
    /// </remarks>
    ValueTask UploadAsync(
        BlobLocation location,
        Stream content,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Uploads multiple blobs to <paramref name="container"/>, returning one identity-carrying result per entry.</summary>
    /// <param name="container">The top-level container the blobs are written to.</param>
    /// <param name="blobs">The blobs to upload. The <see cref="BlobUploadRequest.Path"/> of each is the container-relative key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One <see cref="BlobBulkResult"/> per input blob, each carrying the raw container/path identity and a
    /// <c>Result&lt;bool, Exception&gt;</c> (<c>Ok(true)</c> on success, <c>Fail</c> with the upload exception). Invalid
    /// per-entry paths keep their raw identity with a <see langword="null"/> <see cref="BlobBulkResult.Location"/>. A
    /// per-entry failure does not abort the batch; correlate results by identity, not position.
    /// </returns>
    ValueTask<IReadOnlyList<BlobBulkResult>> BulkUploadAsync(
        string container,
        IReadOnlyCollection<BlobUploadRequest> blobs,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Delete

    /// <summary>Deletes the blob identified by <paramref name="location"/>.</summary>
    /// <param name="location">The blob to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the blob existed and was deleted; <see langword="false"/> if it was not found.</returns>
    ValueTask<bool> DeleteAsync(BlobLocation location, CancellationToken cancellationToken = default);

    /// <summary>Deletes multiple blobs from <paramref name="container"/>, returning one identity-carrying result per entry.</summary>
    /// <param name="container">The top-level container the blobs live in.</param>
    /// <param name="paths">Container-relative object keys to delete. An empty collection returns an empty list immediately.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One <see cref="BlobBulkResult"/> per input key, each carrying the raw container/path identity and a
    /// <c>Result&lt;bool, Exception&gt;</c> (<c>Ok(true)</c> deleted, <c>Ok(false)</c> not found, <c>Fail</c> with the
    /// per-blob exception). Invalid per-entry paths keep their raw identity with a <see langword="null"/>
    /// <see cref="BlobBulkResult.Location"/>. A per-entry failure does not abort the batch; correlate by identity, not
    /// position.
    /// </returns>
    ValueTask<IReadOnlyList<BlobBulkResult>> BulkDeleteAsync(
        string container,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes every blob matched by <paramref name="query"/>'s validated prefix and returns how many were deleted.</summary>
    /// <param name="query">The container plus optional server-pushed prefix selecting the blobs to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of blobs deleted when the whole prefix delete succeeds.</returns>
    /// <remarks>
    /// Deletion is by validated prefix only (the prefix is path-security checked at <see cref="BlobQuery"/>
    /// construction, so a <c>../</c> prefix can never reach enumeration). Glob-pattern deletion is a client-side
    /// concern: list with a glob filter, then bulk-delete. On per-entry failures providers keep attempting every
    /// matched entry, then surface the collected per-entry failures as a single <see cref="AggregateException"/> —
    /// a partial delete always throws instead of silently under-counting.
    /// </remarks>
    ValueTask<int> DeleteAllAsync(BlobQuery query, CancellationToken cancellationToken = default);

    #endregion

    #region Move / Copy

    /// <summary>Moves a blob from <paramref name="source"/> to <paramref name="destination"/>, optionally across containers.</summary>
    /// <param name="source">The blob to move.</param>
    /// <param name="destination">The target location.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the blob was moved; <see langword="false"/> if <paramref name="source"/> was not
    /// found or <paramref name="destination"/> is already occupied.
    /// </returns>
    /// <remarks>
    /// Move rejects an occupied destination: when <paramref name="destination"/> already exists the move returns
    /// <see langword="false"/> without touching either blob (delete-then-move, or <see cref="CopyAsync"/> for
    /// overwrite semantics). This pre-check is <b>non-atomic</b> on every provider except Redis (whose move is a single
    /// atomic script): a destination created concurrently between the check and the copy may still be overwritten, so a
    /// caller needing a hard no-overwrite guarantee must serialize moves to a key. Move is otherwise a non-atomic
    /// copy-then-delete; if deleting the source fails after a successful copy, the implementation makes a best-effort
    /// attempt to roll back by deleting the destination copy so the original is preserved. A resolved self-move (source
    /// and destination resolving to the same backend address) is a no-op returning <see langword="true"/>. On
    /// filesystem-like providers the metadata sidecar moves with the blob.
    /// </remarks>
    ValueTask<bool> MoveAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    );

    /// <summary>Copies a blob from <paramref name="source"/> to <paramref name="destination"/>, leaving the source intact.</summary>
    /// <param name="source">The blob to copy.</param>
    /// <param name="destination">The target location.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the blob was copied; <see langword="false"/> if <paramref name="source"/> was not found.</returns>
    ValueTask<bool> CopyAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Exists

    /// <summary>Determines whether the blob identified by <paramref name="location"/> exists.</summary>
    /// <param name="location">The blob to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the blob exists; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> ExistsAsync(BlobLocation location, CancellationToken cancellationToken = default);

    #endregion

    #region Download / Info

    /// <summary>Opens a readable stream for the blob identified by <paramref name="location"/>, or returns <see langword="null"/> when it does not exist.</summary>
    /// <param name="location">The blob to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="BlobDownloadResult"/> with the content stream and metadata, or <see langword="null"/> when the blob
    /// is not found. The caller owns the result and must dispose it promptly — open streams may hold network
    /// connections or SFTP pool slots depending on the provider.
    /// </returns>
    /// <remarks>Always dispose the returned result with <c>await using</c> or in a <see langword="finally"/> block.</remarks>
    [MustDisposeResource]
    ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    );

    /// <summary>Retrieves metadata and size information for <paramref name="location"/> without downloading its content.</summary>
    /// <param name="location">The blob to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="BlobInfo"/> record, or <see langword="null"/> if the blob does not exist.</returns>
    ValueTask<BlobInfo?> GetBlobInfoAsync(BlobLocation location, CancellationToken cancellationToken = default);

    #endregion

    #region List

    /// <summary>Returns one page of blobs matching <paramref name="query"/>, plus an opaque continuation token.</summary>
    /// <param name="query">The container, optional prefix, page size, and continuation token describing the page to fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="BlobPage"/> carrying the page's blobs and a continuation token. A <see langword="null"/> token marks
    /// the last page; otherwise round-trip the token into a new <see cref="BlobQuery"/> to fetch the next page. The
    /// token is opaque and provider-specific — callers must not parse it. Prefer the <c>GetBlobsAsync</c> streaming
    /// extension over manual paging for full enumeration. Item ordering is provider-specific: AWS, Azure, FileSystem,
    /// and SSH return keys in lexicographic order, while Redis's scan-based listing is unordered and may surface a key
    /// twice across pages during a concurrent rehash — do not rely on ordering in provider-agnostic code.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The <see cref="BlobQuery.ContinuationToken"/> is malformed — not an opaque token produced by a provider's
    /// <see cref="ListAsync"/>. Every provider wraps its native cursor in a shared envelope, so a forged or corrupted
    /// token (a common risk when the token round-trips through a web pagination boundary) fails uniformly with this
    /// catchable contract error rather than leaking a backend SDK exception. Residual: a syntactically valid token
    /// produced by a different provider or store is indistinguishable from a real one, so the result is
    /// backend-defined — typically an empty page or a backend error.
    /// </exception>
    ValueTask<BlobPage> ListAsync(BlobQuery query, CancellationToken cancellationToken = default);

    #endregion
}
