// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Primitives;

namespace Framework.Blobs;

using JetBrainsPure = PureAttribute;
using SystemPure = System.Diagnostics.Contracts.PureAttribute;

[PublicAPI]
public interface IBlobStorage : IDisposable
{
    #region Create Container

    [SystemPure]
    [JetBrainsPure]
    ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default);

    #endregion

    #region Upload

    [SystemPure]
    [JetBrainsPure]
    ValueTask UploadAsync(string[] container, BlobUploadRequest blob, CancellationToken cancellationToken = default);

    #endregion

    #region Bulk Upload

    [SystemPure]
    [JetBrainsPure]
    ValueTask<IReadOnlyList<Result<Exception>>> BulkUploadAsync(
        string[] container,
        IReadOnlyCollection<BlobUploadRequest> blobs,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Delete

    [SystemPure]
    [JetBrainsPure]
    ValueTask<bool> DeleteAsync(string[] container, string blobName, CancellationToken cancellationToken = default);

    #endregion

    #region Bulk Delete

    [SystemPure]
    [JetBrainsPure]
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

    [SystemPure]
    [JetBrainsPure]
    ValueTask<bool> RenameAsync(
        string[] blobContainer,
        string blobName,
        string[] newBlobContainer,
        string newBlobName,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Copy

    [SystemPure]
    [JetBrainsPure]
    ValueTask<bool> CopyAsync(
        string[] blobContainer,
        string blobName,
        string[] newBlobContainer,
        string newBlobName,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Exists

    [SystemPure]
    [JetBrainsPure]
    ValueTask<bool> ExistsAsync(string[] container, string blobName, CancellationToken cancellationToken = default);

    #endregion

    #region Download

    [SystemPure]
    [JetBrainsPure]
    ValueTask<BlobDownloadResult?> DownloadAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region List

    /// <summary>Get page</summary>
    /// <param name="containers">Container directory to paginate.</param>
    /// <param name="blobSearchPattern">
    /// The search string to match against the names of files in path. This parameter can contain
    /// a combination of valid literal path and wildcard (* and ?) characters, but it doesn't
    /// regular expressions.
    /// </param>
    /// <param name="pageSize">Size of the page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [SystemPure]
    [JetBrainsPure]
    ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] containers,
        string? blobSearchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    );

    #endregion
}
