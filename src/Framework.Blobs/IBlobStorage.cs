namespace Framework.Blobs;

using JetBrainsPure = PureAttribute;
using SystemPure = System.Diagnostics.Contracts.PureAttribute;

public interface IBlobStorage
{
    [SystemPure, JetBrainsPure]
    ValueTask CreateContainerAsync(string[] container);

    [SystemPure, JetBrainsPure]
    ValueTask<bool> UploadAsync(
        BlobUploadRequest blob,
        string[] container,
        CancellationToken cancellationToken = default
    );

    [SystemPure, JetBrainsPure]
    ValueTask<IReadOnlyList<bool>> BulkUploadAsync(
        IReadOnlyCollection<BlobUploadRequest> blobs,
        string[] container,
        CancellationToken cancellationToken = default
    );

    [SystemPure, JetBrainsPure]
    ValueTask<IReadOnlyList<bool>> BulkDeleteAsync(
        IReadOnlyCollection<string> blobNames,
        string[] container,
        CancellationToken cancellationToken = default
    );

    [SystemPure, JetBrainsPure]
    ValueTask<BlobDownloadResult?> DownloadAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    );

    [SystemPure, JetBrainsPure]
    ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] container,
        string? searchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    );

    [SystemPure, JetBrainsPure]
    ValueTask<bool> CopyFileAsync(
        string blobName,
        string[] blobContainer,
        string newBlobName,
        string[] newBlobContainer,
        CancellationToken cancellationToken = default
    );

    [SystemPure, JetBrainsPure]
    ValueTask<bool> RenameFileAsync(
        string blobName,
        string[] blobContainer,
        string newBlobName,
        string[] newBlobContainer,
        CancellationToken cancellationToken = default
    );

    [SystemPure, JetBrainsPure]
    ValueTask<bool> ExistsAsync(string blobName, string[] container, CancellationToken cancellationToken = default);

    [SystemPure, JetBrainsPure]
    ValueTask<bool> DeleteAsync(string blobName, string[] container, CancellationToken cancellationToken = default);
}
