using Framework.Arguments;
using Framework.Blobs.FileSystem.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Framework.Blobs.FileSystem;

public sealed class FileSystemBlobStorage(IOptions<FileSystemBlobStorageOptions> options) : IBlobStorage
{
    private readonly AsyncLock _lock = new();
    private static readonly Lazy<char[]> _InvalidPathChars = new(Path.GetInvalidPathChars);
    private readonly string _basePath = options.Value.BaseDirectoryPath;

    private readonly ILogger _logger =
        options.Value.LoggerFactory?.CreateLogger(typeof(FileSystemBlobStorage)) ?? NullLogger.Instance;

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

        var results = blobNames.Select(fileName => _Delete(_BuildPath(container, fileName))).ToList();

        return ValueTask.FromResult<IReadOnlyList<bool>>(results);
    }

    public ValueTask CreateContainerAsync(string[] container)
    {
        Argument.IsNotNullOrEmpty(container);

        var directoryPath = _GetDirectoryPath(container);

        Directory.CreateDirectory(directoryPath);

        return ValueTask.CompletedTask;
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

    public async ValueTask<bool> RenameFileAsync(
        string blobName,
        string[] blobContainer,
        string newBlobName,
        string[] newBlobContainer,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsNotNullOrWhiteSpace(blobName);
        Argument.IsNotNullOrWhiteSpace(newBlobName);
        Argument.IsNotNullOrEmpty(blobContainer);
        Argument.IsNotNullOrEmpty(newBlobContainer);

        var oldFullPath = _BuildPath(blobContainer, blobName).NormalizePath();
        var newFullPath = _BuildPath(newBlobContainer, newBlobName).NormalizePath();
        _logger.LogInformation("Renaming {Path} to {NewPath}", oldFullPath, newFullPath);

        try
        {
            using (await _lock.LockAsync(cancellationToken))
            {
                var directoryPath = _GetDirectoryPath(newBlobContainer);
                Directory.CreateDirectory(directoryPath);

                try
                {
                    File.Move(oldFullPath, newFullPath);
                }
                catch (IOException)
                {
                    File.Delete(newFullPath); // Delete the file if it already exists
                    _logger.LogTrace("Renaming {Path} to {NewPath}", oldFullPath, newFullPath);
                    File.Move(oldFullPath, newFullPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renaming {Path} to {NewPath}", oldFullPath, newFullPath);

            return false;
        }

        return true;
    }

    public ValueTask<bool> ExistsAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = _BuildPath(container, blobName);
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
        var filePath = _BuildPath(container, blobName);
        var delete = _Delete(filePath);

        return ValueTask.FromResult(delete);
    }

    public async ValueTask<BlobUploadResult?> CopyFileAsync(
        string blobName,
        string[] blobContainer,
        string newBlobName,
        string[] newBlobContainer,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsNotNullOrWhiteSpace(blobName);
        Argument.IsNotNullOrWhiteSpace(newBlobName);
        Argument.IsNotNullOrEmpty(blobContainer);
        Argument.IsNotNullOrEmpty(newBlobContainer);

        var blobPath = _BuildPath(blobContainer, blobName);
        var targetPath = _BuildPath(newBlobContainer, newBlobName);

        _logger.LogInformation("Copying {Path} to {TargetPath}", blobPath, targetPath);

        try
        {
            using (await _lock.LockAsync(cancellationToken))
            {
                var targetDirectory = _GetDirectoryPath(newBlobContainer);
                Directory.CreateDirectory(targetDirectory);
                File.Copy(blobPath, targetPath, true);

                return new(newBlobName, newBlobName, new FileInfo(targetPath).Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying {Path} to {TargetPath}", blobPath, targetPath);

            return null;
        }
    }

    public ValueTask<BlobDownloadResult?> DownloadAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        var filePath = _BuildPath(container, blobName);

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

    public async ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] container,
        string? searchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (pageSize <= 0)
        {
            return PagedFileListResult.Empty;
        }

        if (string.IsNullOrEmpty(searchPattern))
        {
            searchPattern = "*";
        }

        searchPattern = searchPattern.NormalizePath();

        var directoryPath = _GetDirectoryPath(container);
        var completePath = Path.GetDirectoryName(Path.Combine(directoryPath, searchPattern));

        if (!Directory.Exists(completePath))
        {
            _logger.LogTrace("Returning empty file list matching {SearchPattern}: Directory Not Found", searchPattern);
            return PagedFileListResult.Empty;
        }

        var result = new PagedFileListResult(_ =>
            Task.FromResult(_GetFiles(directoryPath, searchPattern, 1, pageSize))
        );

        await result.NextPageAsync();

        return result;
    }

    #region Helpers

    private NextPageResult _GetFiles(string directoryPath, string searchPattern, int page, int pageSize)
    {
        var list = new List<BlobSpecification>();

        var pagingLimit = pageSize;
        var skip = (page - 1) * pagingLimit;

        if (pagingLimit < int.MaxValue)
        {
            pagingLimit++;
        }

        _logger.LogTrace(
            "Getting file list matching {SearchPattern} Page: {Page}, PageSize: {PageSize}",
            searchPattern,
            page,
            pageSize
        );

        foreach (
            var path in Directory
                .EnumerateFiles(directoryPath, searchPattern, SearchOption.AllDirectories)
                .Skip(skip)
                .Take(pagingLimit)
        )
        {
            var info = new FileInfo(path);

            if (!info.Exists)
            {
                continue;
            }

            list.Add(
                new()
                {
                    Path = info.FullName.Replace(directoryPath, string.Empty, StringComparison.Ordinal),
                    Created = info.CreationTimeUtc,
                    Modified = info.LastWriteTimeUtc,
                    Size = info.Length
                }
            );
        }

        var hasMore = false;

        if (list.Count == pagingLimit)
        {
            hasMore = true;
            list.RemoveAt(pagingLimit - 1);
        }

        return new()
        {
            Success = true,
            HasMore = hasMore,
            Files = list,
            NextPageFunc = hasMore
                ? _ => Task.FromResult(_GetFiles(directoryPath, searchPattern, page + 1, pageSize))
                : null
        };
    }

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

    private string _BuildPath(string[] container, string fileName)
    {
        Argument.IsNotNullOrWhiteSpace(fileName);
        Argument.IsNotNullOrEmpty(container);

        var filePath = Path.Combine(_basePath, Path.Combine(container), fileName);

        return filePath;
    }

    #endregion
}
