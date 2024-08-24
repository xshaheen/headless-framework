using Framework.BuildingBlocks;

namespace Framework.Blobs;

using JetBrainsPure = PureAttribute;
using SystemPure = System.Diagnostics.Contracts.PureAttribute;

// TODO: Add List
// TODO: Add Sync versions and use them in the DataProtectionXmlRepository

public interface IBlobStorage : IDisposable
{
    [SystemPure, JetBrainsPure]
    ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default);

    [SystemPure, JetBrainsPure]
    ValueTask<IReadOnlyList<Result<Exception>>> BulkUploadAsync(
        IReadOnlyCollection<BlobUploadRequest> blobs,
        string[] container,
        CancellationToken cancellationToken = default
    );

    [SystemPure, JetBrainsPure]
    ValueTask<IReadOnlyList<Result<bool, Exception>>> BulkDeleteAsync(
        IReadOnlyCollection<string> blobNames,
        string[] container,
        CancellationToken cancellationToken = default
    );

    [SystemPure, JetBrainsPure]
    ValueTask UploadAsync(BlobUploadRequest blob, string[] container, CancellationToken cancellationToken = default);

    [SystemPure, JetBrainsPure]
    ValueTask<bool> DeleteAsync(string blobName, string[] container, CancellationToken cancellationToken = default);

    [SystemPure, JetBrainsPure]
    ValueTask<bool> CopyAsync(
        string blobName,
        string[] blobContainer,
        string newBlobName,
        string[] newBlobContainer,
        CancellationToken cancellationToken = default
    );

    [SystemPure, JetBrainsPure]
    ValueTask<bool> RenameAsync(
        string blobName,
        string[] blobContainer,
        string newBlobName,
        string[] newBlobContainer,
        CancellationToken cancellationToken = default
    );

    [SystemPure, JetBrainsPure]
    ValueTask<bool> ExistsAsync(string blobName, string[] container, CancellationToken cancellationToken = default);

    [SystemPure, JetBrainsPure]
    ValueTask<BlobDownloadResult?> DownloadAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get page</summary>
    /// <param name="containers">Container directory to paginate.</param>
    /// <param name="searchPattern">
    /// The search string to match against the names of files in path. This parameter can contain
    /// a combination of valid literal path and wildcard (* and ?) characters, but it doesn't
    /// regular expressions.
    /// </param>
    /// <param name="pageSize">Size of the page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [SystemPure, JetBrainsPure]
    ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] containers,
        string? searchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    );
}
