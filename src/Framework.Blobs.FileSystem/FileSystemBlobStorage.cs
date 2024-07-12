using Framework.Arguments;
using Framework.Blobs.FileSystem.Internals;
using Microsoft.AspNetCore.Hosting;

namespace Framework.Blobs.FileSystem;

public sealed class FileSystemBlobStorage(IWebHostEnvironment env) : IBlobStorage
{
    private static readonly Lazy<char[]> _InvalidPathChars = new(Path.GetInvalidPathChars);
    private readonly string _basePath = env.WebRootPath;

    public async ValueTask<IReadOnlyList<BlobUploadResult>> BulkUploadAsync(
        IReadOnlyCollection<BlobUploadRequest> blobs,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobs);
        Argument.IsNotNullOrEmpty(container);

        var directoryPath = _GetDirectoryPath(container);

        var result = await blobs
            .Select(blob => (blob.Stream, blob.FileName))
            .SaveToLocalFileAsync(directoryPath, cancellationToken);

        return result.ConvertAll(x => new BlobUploadResult(x.SavedName, x.DisplayName, x.Size));
    }

    public ValueTask<IReadOnlyList<bool>> BulkDeleteAsync(
        IReadOnlyCollection<string> blobNames,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var results = blobNames.Select(fileName => _Delete(_GetFilePath(container, fileName))).ToList();

        return ValueTask.FromResult<IReadOnlyList<bool>>(results);
    }

    public async ValueTask<BlobUploadResult> UploadAsync(
        BlobUploadRequest blob,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blob);
        Argument.IsNotNullOrEmpty(container);

        var directoryPath = _GetDirectoryPath(container);

        var (savedName, displayName, size) = await blob.Stream.SaveToLocalFileAsync(
            directoryPath,
            blob.FileName,
            cancellationToken
        );

        return new(savedName, displayName, size);
    }

    public ValueTask<bool> ExistsAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = _GetFilePath(container, blobName);
        var exists = File.Exists(filePath);

        return ValueTask.FromResult(exists);
    }

    public ValueTask<bool> DeleteAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = _GetFilePath(container, blobName);
        var delete = _Delete(filePath);

        return ValueTask.FromResult(delete);
    }

    public ValueTask<BlobDownloadResult?> DownloadAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        var filePath = _GetFilePath(container, blobName);

        return FileStreamExtensions.IoRetryPipeline.ExecuteAsync(
            static async (filePath, token) =>
            {
                if (!File.Exists(filePath))
                {
                    return null;
                }

                await using var fileStream = File.OpenRead(filePath);
                var memoryStream = await fileStream.CopyToMemoryStreamAndFlushAsync(token);

                return new BlobDownloadResult(memoryStream!, Path.GetFileName(filePath));
            },
            filePath,
            cancellationToken
        );
    }

    #region Helpers

    private static bool _Delete(string filePath)
    {
        var possiblePath = filePath.IndexOfAny(_InvalidPathChars.Value) == -1;

        if (!possiblePath)
        {
            return false;
        }

        if (!File.Exists(filePath))
        {
            return false;
        }

        File.Delete(filePath);

        return true;
    }

    private string _GetDirectoryPath(string[] container)
    {
        Argument.IsNotNullOrEmpty(container);

        var filePath = Path.Combine(_basePath, Path.Combine(container));

        return filePath;
    }

    private string _GetFilePath(string[] container, string fileName)
    {
        Argument.IsNotNullOrWhiteSpace(fileName);
        Argument.IsNotNullOrEmpty(container);

        var filePath = Path.Combine(_basePath, Path.Combine(container), fileName);

        return filePath;
    }

    #endregion
}
