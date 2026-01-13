// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Framework.Checks;
using Framework.IO;
using Framework.Primitives;
using Framework.Urls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using File = System.IO.File;

namespace Framework.Blobs.FileSystem;

public sealed class FileSystemBlobStorage(
    IOptions<FileSystemBlobStorageOptions> optionsAccessor,
    IBlobNamingNormalizer normalizer,
    ILogger<FileSystemBlobStorage> logger
) : IBlobStorage
{
    private readonly string _basePath = optionsAccessor.Value.BaseDirectoryPath.NormalizePath()
        .EnsureEndsWith(Path.DirectorySeparatorChar);
    private readonly IBlobNamingNormalizer _normalizer = normalizer;
    private readonly ILogger _logger = logger;

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
        string blobName,
        Stream stream,
        Dictionary<string, string?>? metadata = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobName);
        Argument.IsNotNullOrEmpty(container);

        var directoryPath = _GetDirectoryPath(container);

        await stream.SaveToLocalFileAsync(blobName, directoryPath, cancellationToken).AnyContext();
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
            .SaveToLocalFileAsync(directoryPath, cancellationToken)
            .AnyContext();

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
        Argument.IsNotNullOrEmpty(container);
        Argument.IsNotNullOrWhiteSpace(blobName);

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
        Argument.IsNotNullOrEmpty(container);
        cancellationToken.ThrowIfCancellationRequested();

        if (blobNames.Count == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<Result<bool, Exception>>>([]);
        }

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

        _ThrowIfPathTraversal(path, nameof(blobSearchPattern));

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

    public ValueTask<bool> RenameAsync(
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

        _logger.LogTrace("Renaming {Path} to {NewPath}", oldFullPath, newFullPath);

        if (!File.Exists(oldFullPath))
        {
            return ValueTask.FromResult(false);
        }

        Directory.CreateDirectory(newDirectoryPath);
        File.Move(oldFullPath, newFullPath, overwrite: true);

        return ValueTask.FromResult(true);
    }

    #endregion

    #region Copy

    public ValueTask<bool> CopyAsync(
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

        _logger.LogTrace("Copying {Path} to {TargetPath}", blobPath, targetPath);

        if (!File.Exists(blobPath))
        {
            return ValueTask.FromResult(false);
        }

        Directory.CreateDirectory(targetDirectory);
        File.Copy(blobPath, targetPath, overwrite: true);

        return ValueTask.FromResult(true);
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
        Argument.IsNotNullOrEmpty(container);
        Argument.IsNotNullOrWhiteSpace(blobName);

        var filePath = _BuildBlobPath(container, blobName);
        var exists = File.Exists(filePath);

        return ValueTask.FromResult(exists);
    }

    #endregion

    #region Download

    public ValueTask<BlobDownloadResult?> DownloadAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var filePath = _BuildBlobPath(container, blobName);

        if (!File.Exists(filePath))
        {
            return ValueTask.FromResult<BlobDownloadResult?>(null);
        }

        var fileStream = File.OpenRead(filePath);

        return ValueTask.FromResult<BlobDownloadResult?>(
            new BlobDownloadResult(fileStream, Path.GetFileName(filePath))
        );
    }

    public ValueTask<BlobInfo?> GetBlobInfoAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        var directoryPath = _GetDirectoryPath(container);
        var filePath = Path.Combine(directoryPath, blobName);

        _logger.LogTrace("Getting file stream for {Path}", filePath);
        var fileInfo = new FileInfo(filePath);

        if (!fileInfo.Exists)
        {
            _logger.LogError("Unable to get file info for {Path}: File Not Found", filePath);

            return ValueTask.FromResult<BlobInfo?>(null);
        }

        var blobKey = Url.Combine([.. container.Skip(1).Append(blobName)]);

        return ValueTask.FromResult<BlobInfo?>(_CreateBlobInfo(fileInfo, blobKey));
    }

    #endregion

    #region List

    public async IAsyncEnumerable<BlobInfo> GetBlobsAsync(
        string[] container,
        string? blobSearchPattern = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);

        if (string.IsNullOrEmpty(blobSearchPattern))
        {
            blobSearchPattern = "*";
        }

        blobSearchPattern = blobSearchPattern.NormalizePath();

        var baseDirectoryPath = Path.Combine(_basePath, container[0]).EnsureEndsWith(Path.DirectorySeparatorChar);
        var directoryPath = _GetDirectoryPath(container);
        var completePath = Path.GetDirectoryName(Path.Combine(directoryPath, blobSearchPattern));

        if (!Directory.Exists(completePath))
        {
            yield break;
        }

        await ValueTask.CompletedTask;

        foreach (var path in Directory.EnumerateFiles(directoryPath, blobSearchPattern, SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(path);

            if (!fileInfo.Exists)
            {
                continue;
            }

            var blobKey = fileInfo
                .FullName.Replace(baseDirectoryPath, string.Empty, StringComparison.Ordinal)
                .Replace('\\', '/');

            yield return _CreateBlobInfo(fileInfo, blobKey);
        }
    }

    /// <summary>
    /// Gets a paged list of blobs in the specified container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Performance Warning:</strong> This method has O(n^2) complexity for full enumeration
    /// of large directories. Each page request re-enumerates the directory from the start and skips
    /// previous items. For example, fetching page 10 with pageSize=100 reads 1000 file entries to
    /// return 100 results.
    /// </para>
    /// <para>
    /// For directories with many files, consider using <see cref="GetBlobsAsync"/> with LINQ
    /// operators, or implement application-level caching of the file list.
    /// </para>
    /// </remarks>
    public async ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] container,
        string? blobSearchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsNotNullOrEmpty(container);
        Argument.IsPositive(pageSize);
        Argument.IsLessThanOrEqualTo(pageSize, int.MaxValue - 1);

        if (string.IsNullOrEmpty(blobSearchPattern))
        {
            blobSearchPattern = "*";
        }

        blobSearchPattern = blobSearchPattern.NormalizePath();

        var baseDirectoryPath = Path.Combine(_basePath, container[0]).EnsureEndsWith(Path.DirectorySeparatorChar);
        var directoryPath = _GetDirectoryPath(container);
        var completePath = Path.GetDirectoryName(Path.Combine(directoryPath, blobSearchPattern));

        if (!Directory.Exists(completePath))
        {
            _logger.LogTrace(
                "Returning empty file list matching {SearchPattern}: Directory Not Found",
                blobSearchPattern
            );

            return PagedFileListResult.Empty;
        }

        var result = new PagedFileListResult(
            (_, _) =>
                ValueTask.FromResult<INextPageResult>(
                    _GetFiles(baseDirectoryPath, directoryPath, blobSearchPattern, 1, pageSize)
                )
        );

        await result.NextPageAsync(cancellationToken).AnyContext();

        return result;
    }

    private NextPageResult _GetFiles(
        string baseDirectoryPath,
        string directoryPath,
        string searchPattern,
        int page,
        int pageSize
    )
    {
        var list = new List<BlobInfo>();

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
            var fileInfo = new FileInfo(path);

            if (!fileInfo.Exists)
            {
                continue;
            }

            var blobKey = fileInfo
                .FullName.Replace(baseDirectoryPath, string.Empty, StringComparison.Ordinal)
                .Replace('\\', '/');

            list.Add(_CreateBlobInfo(fileInfo, blobKey));
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
                ? (_, _) =>
                    ValueTask.FromResult<INextPageResult>(
                        _GetFiles(baseDirectoryPath, directoryPath, searchPattern, page + 1, pageSize)
                    )
                : null,
        };
    }

    #endregion

    #region Helpers

    private static BlobInfo _CreateBlobInfo(FileInfo fileInfo, string blobKey) => new()
    {
        BlobKey = blobKey,
        Created = new DateTimeOffset(fileInfo.CreationTimeUtc, TimeSpan.Zero),
        Modified = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
        Size = fileInfo.Length,
    };

    #endregion

    #region Build Paths

    private string _BuildBlobPath(string[] container, string fileName)
    {
        Argument.IsNotNullOrWhiteSpace(fileName);
        Argument.IsNotNullOrEmpty(container);

        var normalizedFileName = _normalizer.NormalizeBlobName(fileName);

        // Use single Path.Combine call to avoid intermediate string allocations
        var segments = new string[container.Length + 2];
        segments[0] = _basePath;
        for (var i = 0; i < container.Length; i++)
        {
            segments[i + 1] = _normalizer.NormalizeContainerName(container[i]);
        }
        segments[^1] = normalizedFileName;

        var path = Path.Combine(segments);
        _ThrowIfPathTraversal(path, nameof(fileName));

        return path;
    }

    private string _GetDirectoryPath(string[] container)
    {
        Argument.IsNotNullOrEmpty(container);

        var normalizedContainer = container.Select(_normalizer.NormalizeContainerName).ToArray();

        var filePath = Path.Combine(_basePath, Path.Combine(normalizedContainer));
        _ThrowIfPathTraversal(filePath, nameof(container));

        return filePath.EnsureEndsWith(Path.DirectorySeparatorChar);
    }

    private void _ThrowIfPathTraversal(string path, string paramName)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Path traversal detected", paramName);
        }
    }

    #endregion

    #region Dispose

    public void Dispose() { }

    #endregion
}
