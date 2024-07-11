namespace Framework.Blobs;

using JetBrainsPure = PureAttribute;
using SystemPure = System.Diagnostics.Contracts.PureAttribute;

public interface IBlobStorage
{
    [SystemPure, JetBrainsPure]
    ValueTask<BlobUploadResult> UploadAsync(
        BlobUploadRequest blob,
        string[] container,
        CancellationToken cancellationToken = default
    );

    [SystemPure, JetBrainsPure]
    ValueTask<IReadOnlyList<BlobUploadResult>> BulkUploadAsync(
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
    ValueTask<bool> ExistsAsync(string blobName, string[] container, CancellationToken cancellationToken = default);

    [SystemPure, JetBrainsPure]
    ValueTask<bool> DeleteAsync(string blobName, string[] container, CancellationToken cancellationToken = default);
}
