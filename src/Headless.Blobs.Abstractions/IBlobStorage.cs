// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Blobs;

/// <summary>
/// Provider-agnostic contract for blob storage operations: upload, download, delete, rename, copy, list, and existence checks.
/// </summary>
/// <remarks>
/// Container paths are expressed as a <see langword="string[]"/> of hierarchical segments. The first segment maps to the
/// provider-level root container (S3 bucket, Azure container, SFTP root directory, Redis hash key prefix, file-system
/// root sub-directory). Additional segments are treated as path components within that root. Implementations apply
/// provider-specific naming rules to each segment via the registered <see cref="IBlobNamingNormalizer"/>.
/// </remarks>
[PublicAPI]
public interface IBlobStorage : IAsyncDisposable
{
    #region Create Container

    /// <summary>
    /// Ensures the container identified by <paramref name="container"/> exists, creating it if necessary.
    /// </summary>
    /// <param name="container">Hierarchical path segments identifying the container to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The operation is idempotent — calling it on an already-existing container is safe. Implementations that
    /// cache the result of a successful create (for example to avoid repeated HEAD+PUT round trips on S3) will
    /// also record this explicit call in the per-instance cache.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="container"/> is null or empty, or when a segment fails path-security validation.
    /// </exception>
    ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default);

    #endregion

    #region Upload

    /// <summary>
    /// Uploads <paramref name="stream"/> as a blob named <paramref name="blobName"/> inside <paramref name="container"/>.
    /// </summary>
    /// <param name="container">Hierarchical path segments identifying the target container.</param>
    /// <param name="blobName">The blob's file name within the container. Must not be null, empty, or contain path-traversal sequences.</param>
    /// <param name="stream">The content to upload. Seekable streams are rewound to position 0 before upload; non-seekable streams are buffered to memory first.</param>
    /// <param name="metadata">Optional key/value metadata to store alongside the blob. Providers that do not support metadata (for example SFTP) silently ignore this.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="blobName"/> or <paramref name="container"/> is null or empty, or when either fails path-security validation.
    /// </exception>
    ValueTask UploadAsync(
        string[] container,
        string blobName,
        Stream stream,
        Dictionary<string, string?>? metadata = null,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Bulk Upload

    /// <summary>
    /// Uploads multiple blobs to <paramref name="container"/> in parallel, returning one result per entry.
    /// </summary>
    /// <param name="container">Hierarchical path segments identifying the target container.</param>
    /// <param name="blobs">The blobs to upload. Must not be null or empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A list with one <see cref="Result{TError}"/> per input blob, in original order. Each entry is either
    /// <c>Ok</c> on success or <c>Fail</c> with the upload exception; a per-blob failure does not abort the batch.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="blobs"/> or <paramref name="container"/> is null or empty.
    /// </exception>
    ValueTask<IReadOnlyList<Result<Exception>>> BulkUploadAsync(
        string[] container,
        IReadOnlyCollection<BlobUploadRequest> blobs,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Delete

    /// <summary>
    /// Deletes the blob named <paramref name="blobName"/> from <paramref name="container"/>.
    /// </summary>
    /// <param name="container">Hierarchical path segments identifying the container.</param>
    /// <param name="blobName">The blob name to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the blob existed and was deleted; <see langword="false"/> if it was not found.</returns>
    ValueTask<bool> DeleteAsync(string[] container, string blobName, CancellationToken cancellationToken = default);

    #endregion

    #region Bulk Delete

    /// <summary>
    /// Deletes multiple blobs from <paramref name="container"/> in parallel, returning one result per entry.
    /// </summary>
    /// <param name="container">Hierarchical path segments identifying the container.</param>
    /// <param name="blobNames">Names of the blobs to delete. An empty collection returns an empty list immediately.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A list with one result per input name. Each entry is <c>Ok(<see langword="true"/>)</c> when deleted,
    /// <c>Ok(<see langword="false"/>)</c> when not found, or <c>Fail</c> with the per-blob exception.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="container"/> is null or empty.</exception>
    ValueTask<IReadOnlyList<Result<bool, Exception>>> BulkDeleteAsync(
        string[] container,
        IReadOnlyCollection<string> blobNames,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes all blobs in <paramref name="container"/> that match <paramref name="blobSearchPattern"/>, or every blob
    /// in the container when no pattern is supplied.
    /// </summary>
    /// <param name="container">Hierarchical path segments identifying the container.</param>
    /// <param name="blobSearchPattern">
    /// Optional glob pattern (<c>*</c> and <c>?</c> wildcards) matched against blob names. Pass <see langword="null"/>
    /// or <c>"*"</c> to delete everything in the container.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of blobs successfully deleted.</returns>
    /// <remarks>
    /// Best-effort bulk deletion: the returned count reflects only blobs confirmed deleted. The intended contract is
    /// that an individual blob which fails to delete does not abort the operation and is excluded from the count,
    /// while a failure that prevents the operation as a whole (authentication, connectivity, or container access)
    /// propagates as an exception. Callers needing a per-blob outcome should use <see cref="BulkDeleteAsync"/>.
    /// NOTE: providers do not yet uniformly honor this — some currently throw on any per-blob failure and others
    /// swallow all errors; that divergence is tracked for reconciliation.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="container"/> is null or empty.</exception>
    ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Rename

    /// <summary>
    /// Moves a blob from one location to another, optionally across containers, as an atomic copy-then-delete.
    /// </summary>
    /// <param name="blobContainer">Hierarchical path segments identifying the source container.</param>
    /// <param name="blobName">The source blob name.</param>
    /// <param name="newBlobContainer">Hierarchical path segments identifying the destination container.</param>
    /// <param name="newBlobName">The destination blob name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the blob was moved; <see langword="false"/> if the source blob was not found.
    /// </returns>
    /// <remarks>
    /// Implementations perform a copy followed by deletion of the source. If the source deletion fails after a
    /// successful copy, the implementation attempts to roll back by deleting the destination copy so the original
    /// is preserved.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when any name or container argument is null, empty, or fails path-security validation.
    /// </exception>
    ValueTask<bool> RenameAsync(
        string[] blobContainer,
        string blobName,
        string[] newBlobContainer,
        string newBlobName,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Copy

    /// <summary>
    /// Copies a blob from one location to another, optionally across containers, leaving the source intact.
    /// </summary>
    /// <param name="blobContainer">Hierarchical path segments identifying the source container.</param>
    /// <param name="blobName">The source blob name.</param>
    /// <param name="newBlobContainer">Hierarchical path segments identifying the destination container.</param>
    /// <param name="newBlobName">The destination blob name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the blob was copied; <see langword="false"/> if the source blob was not found.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when any name or container argument is null, empty, or fails path-security validation.
    /// </exception>
    ValueTask<bool> CopyAsync(
        string[] blobContainer,
        string blobName,
        string[] newBlobContainer,
        string newBlobName,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Exists

    /// <summary>
    /// Determines whether the blob identified by <paramref name="blobName"/> exists in <paramref name="container"/>.
    /// </summary>
    /// <param name="container">Hierarchical path segments identifying the container.</param>
    /// <param name="blobName">The blob name to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the blob exists; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> ExistsAsync(string[] container, string blobName, CancellationToken cancellationToken = default);

    #endregion

    #region Download

    /// <summary>
    /// Opens a readable stream for the specified blob, or returns <see langword="null"/> if the blob does not exist.
    /// </summary>
    /// <param name="container">Hierarchical path segments identifying the container.</param>
    /// <param name="blobName">The blob name to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="BlobDownloadResult"/> containing the content stream and metadata, or <see langword="null"/> when
    /// the blob is not found. The caller owns the result and must dispose it promptly — open streams may hold
    /// network connections or SFTP pool slots depending on the provider.
    /// </returns>
    /// <remarks>
    /// Always dispose the returned result with <c>await using</c> or in a <see langword="finally"/> block.
    /// Holding the stream open for extended periods may exhaust connection-pool resources.
    /// </remarks>
    [MustDisposeResource]
    ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Retrieves metadata and size information for the specified blob without downloading its content.
    /// </summary>
    /// <param name="container">Hierarchical path segments identifying the container.</param>
    /// <param name="blobName">The blob name to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="BlobInfo"/> record, or <see langword="null"/> if the blob does not exist.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="blobName"/> or <paramref name="container"/> is <see langword="null"/>.</exception>
    ValueTask<BlobInfo?> GetBlobInfoAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region List

    /// <summary>
    /// Streams all blobs in <paramref name="container"/> that match <paramref name="blobSearchPattern"/> as an
    /// asynchronous sequence, without buffering the full result set in memory.
    /// </summary>
    /// <param name="container">Hierarchical path segments identifying the container to enumerate.</param>
    /// <param name="blobSearchPattern">
    /// Optional glob pattern (<c>*</c> and <c>?</c> wildcards) matched against blob names. Pass <see langword="null"/>
    /// to return all blobs. Regular expressions are not supported.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async sequence of <see cref="BlobInfo"/> records. Prefer this over <see cref="GetPagedListAsync"/> when iterating large datasets.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="container"/> is null or empty.</exception>
    IAsyncEnumerable<BlobInfo> GetBlobsAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the first page of blobs and a cursor for retrieving subsequent pages.
    /// </summary>
    /// <param name="container">Hierarchical path segments identifying the container to paginate.</param>
    /// <param name="blobSearchPattern">
    /// Optional glob pattern (<c>*</c> and <c>?</c> wildcards) matched against blob names. Pass <see langword="null"/>
    /// to return all blobs. Regular expressions are not supported.
    /// </param>
    /// <param name="pageSize">Maximum number of blobs per page. Must be a positive integer less than <see cref="int.MaxValue"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PagedFileListResult"/> containing the first page of blobs and a continuation handle. Call
    /// <see cref="PagedFileListResult.NextPageAsync"/> to fetch subsequent pages while <see cref="PagedFileListResult.HasMore"/> is <see langword="true"/>.
    /// </returns>
    /// <remarks>
    /// On providers without native server-side pagination (file system, SFTP), each page re-enumerates from the
    /// start, giving O(page × pageSize) I/O cost for full enumeration. Use <see cref="GetBlobsAsync"/> instead
    /// when iterating all blobs or working with large directories.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="container"/> is null or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pageSize"/> is not positive.</exception>
    ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] container,
        string? blobSearchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    );

    #endregion
}
