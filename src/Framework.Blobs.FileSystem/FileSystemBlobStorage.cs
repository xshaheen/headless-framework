// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs.FileSystem.Internals;
using Framework.Kernel.BuildingBlocks.Helpers.IO;
using Framework.Kernel.Checks;
using Framework.Kernel.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using File = System.IO.File;

namespace Framework.Blobs.FileSystem;

public sealed class FileSystemBlobStorage(IOptions<FileSystemBlobStorageSettings> options) : IBlobStorage
{
    private readonly AsyncLock _lock = new();
    private readonly string _basePath = options.Value.BaseDirectoryPath;

    private readonly ILogger _logger =
        options.Value.LoggerFactory?.CreateLogger(typeof(FileSystemBlobStorage)) ?? NullLogger.Instance;

    #region Create Container

    public ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsNotNullOrEmpty(container);

        var directoryPath = _GetDirectoryPath(container);

        Directory.CreateDirectory(directoryPath);

        return ValueTask.CompletedTask;
    }

    #endregion

    #region Upload

    public async ValueTask UploadAsync(
        string[] container,
        BlobUploadRequest blob,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blob);
        Argument.IsNotNullOrEmpty(container);

        var directoryPath = _GetDirectoryPath(container);
        await blob.Stream.SaveToLocalFileAsync(blob.FileName, directoryPath, cancellationToken);
    }

    #endregion

    #region Bulk Upload

    public async ValueTask<IReadOnlyList<Result<Exception>>> BulkUploadAsync(
        string[] container,
        IReadOnlyCollection<BlobUploadRequest> blobs,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobs);
        Argument.IsNotNullOrEmpty(container);

        var directoryPath = _GetDirectoryPath(container);

        var result = await blobs
            .Select(blob => (blob.Stream, blob.FileName))
            .SaveToLocalFileAsync(directoryPath, cancellationToken);

        return result;
    }

    #endregion

    #region Delete

    public ValueTask<bool> DeleteAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filePath = _BuildBlobPath(container, blobName);
        var delete = _Delete(filePath);

        return ValueTask.FromResult(delete);
    }

    private static bool _Delete(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        File.Delete(filePath);

        return true;
    }

    #endregion

    #region Bulk Delete

    public ValueTask<IReadOnlyList<Result<bool, Exception>>> BulkDeleteAsync(
        string[] container,
        IReadOnlyCollection<string> blobNames,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<Result<bool, Exception>> results = blobNames
            .Select(fileName =>
            {
                try
                {
                    return _Delete(_BuildBlobPath(container, fileName));
                }
                catch (Exception e)
                {
                    return Result<bool, Exception>.Fail(e);
                }
            })
            .ToList();

        return ValueTask.FromResult(results);
    }

    public ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directoryPath = _GetDirectoryPath(container);

        // No search pattern, delete the entire directory
        if (string.IsNullOrEmpty(blobSearchPattern) || string.Equals(blobSearchPattern, "*", StringComparison.Ordinal))
        {
            if (!Directory.Exists(directoryPath))
            {
                return ValueTask.FromResult(0);
            }

            _logger.LogInformation("Deleting {Directory} directory", directoryPath);

            var count = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories).Count();
            Directory.Delete(directoryPath, recursive: true);

            _logger.LogTrace("Finished deleting {Directory} directory with {FileCount} files", directoryPath, count);

            return ValueTask.FromResult(count);
        }

        blobSearchPattern = blobSearchPattern.NormalizePath();
        var path = Path.Combine(directoryPath, blobSearchPattern);

        // If the pattern is end with directory separator, delete the directory
        if (
            path[^1] == Path.DirectorySeparatorChar
            || path.EndsWith($"{Path.DirectorySeparatorChar}*", StringComparison.Ordinal)
        )
        {
            var directory = Path.GetDirectoryName(path);

            if (!Directory.Exists(directory))
            {
                return ValueTask.FromResult(0);
            }

            _logger.LogInformation("Deleting {Directory} directory", directory);

            var count = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories).Count();
            Directory.Delete(directory, recursive: true);

            _logger.LogTrace("Finished deleting {Directory} directory with {FileCount} files", directory, count);

            return ValueTask.FromResult(count);
        }

        // If the pattern is a directory, delete the directory
        if (Directory.Exists(path))
        {
            _logger.LogInformation("Deleting {Directory} directory", path);

            var count = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Count();
            Directory.Delete(path, recursive: true);

            _logger.LogTrace("Finished deleting {Directory} directory with {FileCount} files", path, count);

            return ValueTask.FromResult(count);
        }

        _logger.LogInformation("Deleting files matching {SearchPattern}", blobSearchPattern);

        var filesCount = 0;

        foreach (var file in Directory.EnumerateFiles(directoryPath, blobSearchPattern, SearchOption.AllDirectories))
        {
            _logger.LogTrace("Deleting {Path}", file);
            File.Delete(file);
            filesCount++;
        }

        _logger.LogTrace("Finished deleting {FileCount} files matching {SearchPattern}", filesCount, blobSearchPattern);

        return ValueTask.FromResult(filesCount);
    }

    #endregion

    #region Rename

    public async ValueTask<bool> RenameAsync(
        string[] blobContainer,
        string blobName,
        string[] newBlobContainer,
        string newBlobName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        Argument.IsNotNullOrWhiteSpace(blobName);
        Argument.IsNotNullOrWhiteSpace(newBlobName);
        Argument.IsNotNullOrEmpty(blobContainer);
        Argument.IsNotNullOrEmpty(newBlobContainer);

        var oldFullPath = _BuildBlobPath(blobContainer, blobName).NormalizePath();
        var newFullPath = _BuildBlobPath(newBlobContainer, newBlobName).NormalizePath();
        var newDirectoryPath = _GetDirectoryPath(newBlobContainer);

        _logger.LogInformation("Renaming {Path} to {NewPath}", oldFullPath, newFullPath);

        try
        {
            using (await _lock.LockAsync(cancellationToken))
            {
                Directory.CreateDirectory(newDirectoryPath);

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

    #endregion

    #region Copy

    public async ValueTask<bool> CopyAsync(
        string[] blobContainer,
        string blobName,
        string[] newBlobContainer,
        string newBlobName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsNotNullOrWhiteSpace(blobName);
        Argument.IsNotNullOrWhiteSpace(newBlobName);
        Argument.IsNotNullOrEmpty(blobContainer);
        Argument.IsNotNullOrEmpty(newBlobContainer);

        var blobPath = _BuildBlobPath(blobContainer, blobName);
        var targetPath = _BuildBlobPath(newBlobContainer, newBlobName);
        var targetDirectory = _GetDirectoryPath(newBlobContainer);

        _logger.LogInformation("Copying {Path} to {TargetPath}", blobPath, targetPath);

        try
        {
            using (await _lock.LockAsync(cancellationToken))
            {
                Directory.CreateDirectory(targetDirectory);
                File.Copy(blobPath, targetPath, overwrite: true);

                return true;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error copying {Path} to {TargetPath}", blobPath, targetPath);

            return false;
        }
    }

    #endregion

    #region Exists

    public ValueTask<bool> ExistsAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filePath = _BuildBlobPath(container, blobName);
        var exists = File.Exists(filePath);

        return ValueTask.FromResult(exists);
    }

    #endregion

    #region Downaload

    public ValueTask<BlobDownloadResult?> DownloadAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var filePath = _BuildBlobPath(container, blobName);

        return FileHelper.IoRetryPipeline.ExecuteAsync(
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

    #endregion

    #region List

    public async ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] containers,
        string? blobSearchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsNotNullOrEmpty(containers);
        Argument.IsPositive(pageSize);

        if (string.IsNullOrEmpty(blobSearchPattern))
        {
            blobSearchPattern = "*";
        }

        blobSearchPattern = blobSearchPattern.NormalizePath();

        var directoryPath = _GetDirectoryPath(containers);
        var completePath = Path.GetDirectoryName(Path.Combine(directoryPath, blobSearchPattern));

        if (!Directory.Exists(completePath))
        {
            _logger.LogTrace(
                "Returning empty file list matching {SearchPattern}: Directory Not Found",
                blobSearchPattern
            );

            return PagedFileListResult.Empty;
        }

        var result = new PagedFileListResult(_ =>
            ValueTask.FromResult<INextPageResult>(_GetFiles(directoryPath, blobSearchPattern, 1, pageSize))
        );

        await result.NextPageAsync();

        return result;
    }

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
                    Size = info.Length,
                }
            );
        }

        var hasMore = false;

        if (list.Count == pagingLimit)
        {
            hasMore = true;
            list.RemoveAt(pagingLimit - 1);
        }

        return new NextPageResult
        {
            Success = true,
            HasMore = hasMore,
            Blobs = list,
            NextPageFunc = hasMore
                ? _ =>
                    ValueTask.FromResult<INextPageResult>(_GetFiles(directoryPath, searchPattern, page + 1, pageSize))
                : null,
        };
    }

    #endregion

    #region Build Paths

    private string _BuildBlobPath(string[] container, string fileName)
    {
        Argument.IsNotNullOrWhiteSpace(fileName);
        Argument.IsNotNullOrEmpty(container);

        var filePath = Path.Combine(_basePath, Path.Combine(container), fileName);

        return filePath;
    }

    private string _GetDirectoryPath(string[] container)
    {
        Argument.IsNotNullOrEmpty(container);

        var filePath = Path.Combine(_basePath, Path.Combine(container));

        return filePath;
    }

    #endregion

    #region Dispose

    public void Dispose() { }

    #endregion
}
