// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Framework.Blobs;

[PublicAPI]
public interface IBlobStorage : IDisposable
{
    #region Create Container

    ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default);

    #endregion

    #region Upload

    ValueTask UploadAsync(
        string[] container,
        string blobName,
        Stream stream,
        Dictionary<string, string?>? metadata = null,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Bulk Upload

    ValueTask<IReadOnlyList<Result<Exception>>> BulkUploadAsync(
        string[] container,
        IReadOnlyCollection<BlobUploadRequest> blobs,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Delete

    ValueTask<bool> DeleteAsync(string[] container, string blobName, CancellationToken cancellationToken = default);

    #endregion

    #region Bulk Delete

    ValueTask<IReadOnlyList<Result<bool, Exception>>> BulkDeleteAsync(
        string[] container,
        IReadOnlyCollection<string> blobNames,
        CancellationToken cancellationToken = default
    );

    ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Rename

    ValueTask<bool> RenameAsync(
        string[] blobContainer,
        string blobName,
        string[] newBlobContainer,
        string newBlobName,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Copy

    ValueTask<bool> CopyAsync(
        string[] blobContainer,
        string blobName,
        string[] newBlobContainer,
        string newBlobName,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Exists

    ValueTask<bool> ExistsAsync(string[] container, string blobName, CancellationToken cancellationToken = default);

    #endregion

    #region Download

    ValueTask<BlobDownloadResult?> DownloadAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    );

    ValueTask<BlobInfo?> GetBlobInfoAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region List

    // /// <summary>
    // /// Stream blobs from container with O(n) performance.
    // /// Recommended for iterating all blobs or large datasets.
    // /// </summary>
    // /// <param name="container">Container directory to enumerate.</param>
    // /// <param name="blobSearchPattern">
    // /// The search string to match against the names of files in a path. This parameter can contain
    // /// a combination of valid literal path and wildcard (* and ?) characters, but it doesn't support
    // /// regular expressions.
    // /// </param>
    // /// <param name="cancellationToken">Cancellation token.</param>
    // IAsyncEnumerable<BlobInfo> GetBlobsAsync(
    //     string[] container,
    //     string? blobSearchPattern = null,
    //     CancellationToken cancellationToken = default
    // );

    /// <summary>
    /// Get page of blobs.
    /// WARNING: Has O(page * pageSize) performance per page due to re-enumeration.
    /// Use GetBlobsAsync() for better performance when iterating all blobs.
    /// </summary>
    /// <param name="container">Container directory to paginate.</param>
    /// <param name="blobSearchPattern">
    /// The search string to match against the names of files in a path. This parameter can contain
    /// a combination of valid literal path and wildcard (* and ?) characters, but it doesn't
    /// regular expressions.
    /// </param>
    /// <param name="pageSize">Size of the page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] container,
        string? blobSearchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    );

    #endregion
}
