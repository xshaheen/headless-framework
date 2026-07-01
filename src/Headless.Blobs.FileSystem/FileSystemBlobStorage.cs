// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Blobs.Internals;
using Headless.Checks;
using Headless.IO;
using Headless.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using File = System.IO.File;

namespace Headless.Blobs.FileSystem;

/// <summary>
/// <see cref="IBlobStorage"/> implementation backed by the local file system.
/// </summary>
/// <remarks>
/// All blobs are stored under the directory configured via <see cref="FileSystemBlobStorageOptions.BaseDirectoryPath"/>.
/// Path-traversal attempts (blob names or container segments that resolve outside the base directory) throw
/// <see cref="ArgumentException"/>. The file system does not support blob metadata — metadata supplied on upload
/// is silently ignored.
/// </remarks>
public sealed class FileSystemBlobStorage(
    IOptions<FileSystemBlobStorageOptions> optionsAccessor,
    IBlobNamingNormalizer normalizer,
    ILogger<FileSystemBlobStorage> logger
) : IBlobStorage
{
    private readonly string _basePath = optionsAccessor
        .Value.BaseDirectoryPath.NormalizePath()
        .EnsureEndsWith(Path.DirectorySeparatorChar);

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

        PathValidation.ValidateContainer(container);
        PathValidation.ValidatePathSegment(blobName);

        var directoryPath = _GetDirectoryPath(container);

        await stream.SaveToLocalFileAsync(blobName, directoryPath, cancellationToken).ConfigureAwait(false);
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

        PathValidation.ValidateContainer(container);

        var directoryPath = _GetDirectoryPath(container);

        // Per-blob name safety is enforced inside FileHelper.SaveToLocalFileAsync, so a malicious file
        // name surfaces as a per-blob Result.Fail rather than failing the whole batch.
        var result = await blobs
            .Select(blob => (blob.Stream, blob.FileName))
            .SaveToLocalFileAsync(directoryPath, cancellationToken)
            .ConfigureAwait(false);

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

        // No search pattern: clear the whole container. Preserve the container directory itself (mirrors the
        // SshNet provider and the object-store providers, which have no directory to remove) and delete only
        // its contents.
        if (string.IsNullOrEmpty(blobSearchPattern) || string.Equals(blobSearchPattern, "*", StringComparison.Ordinal))
        {
            return ValueTask.FromResult(
                _DeleteDirectoryWithLogging(directoryPath, includeSelf: false, cancellationToken)
            );
        }

        // Reject traversal sequences in the pattern before it is combined with the directory. The boundary check
        // below is anchored to the base directory, so a '..' pattern could otherwise resolve into a sibling
        // container; ValidatePathSegment rejects it up front (matching how blob names are validated).
        PathValidation.ValidatePathSegment(blobSearchPattern);

        blobSearchPattern = blobSearchPattern.NormalizePath();
        var path = Path.Combine(directoryPath, blobSearchPattern);

        _ThrowIfPathTraversal(path, nameof(blobSearchPattern));

        // If the pattern ends with directory separator, delete the directory
        if (
            path[^1] == Path.DirectorySeparatorChar
            || path.EndsWith($"{Path.DirectorySeparatorChar}*", StringComparison.Ordinal)
        )
        {
            var directory = Path.GetDirectoryName(path);
            return ValueTask.FromResult(_DeleteDirectoryWithLogging(directory, includeSelf: true, cancellationToken));
        }

        // If the pattern is a directory, delete the directory
        if (Directory.Exists(path))
        {
            return ValueTask.FromResult(_DeleteDirectoryWithLogging(path, includeSelf: true, cancellationToken));
        }

        logger.LogDeletingFilesMatching(blobSearchPattern);

        var filesCount = 0;

        foreach (var file in Directory.EnumerateFiles(directoryPath, blobSearchPattern, SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogDeletingFile(file);
            File.Delete(file);
            filesCount++;
        }

        logger.LogFinishedDeletingFiles(filesCount, blobSearchPattern);

        return ValueTask.FromResult(filesCount);
    }

    private int _DeleteDirectoryWithLogging(
        string? directoryPath,
        bool includeSelf,
        CancellationToken cancellationToken
    )
    {
        if (directoryPath is null || !Directory.Exists(directoryPath))
        {
            return 0;
        }

        logger.LogDeletingDirectory(directoryPath);

        // Count while deleting in a single enumeration pass; the count is the return value, so we cannot skip it,
        // but we no longer walk the tree once to count and again to delete.
        var count = 0;

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(file);
            count++;
        }

        if (includeSelf)
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        else
        {
            // Keep the container directory; remove the now-empty sub-directories it contained.
            foreach (var subDirectory in Directory.EnumerateDirectories(directoryPath))
            {
                Directory.Delete(subDirectory, recursive: true);
            }
        }

        logger.LogFinishedDeletingDirectory(directoryPath, count);

        return count;
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

        var oldFullPath = _BuildBlobPath(blobContainer, blobName);
        var newFullPath = _BuildBlobPath(newBlobContainer, newBlobName);
        var newDirectoryPath = _GetDirectoryPath(newBlobContainer);

        logger.LogRenamingFile(oldFullPath, newFullPath);

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

        logger.LogCopyingFile(blobPath, targetPath);

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

    public ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filePath = _BuildBlobPath(container, blobName);

        try
        {
            var fileStream = File.OpenRead(filePath);

#pragma warning disable CA2000 // Ownership transfers to the returned BlobDownloadResult ([MustDisposeResource]).
            return ValueTask.FromResult<BlobDownloadResult?>(
                new BlobDownloadResult(fileStream, Path.GetFileName(filePath))
            );
#pragma warning restore CA2000
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // The file was removed between the path build and the open (TOCTOU); honor the documented null contract
            // instead of leaking a FileNotFoundException to the caller.
            return ValueTask.FromResult<BlobDownloadResult?>(null);
        }
    }

    public ValueTask<BlobInfo?> GetBlobInfoAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        // Reject traversal blob names before they are combined with the container directory. This mirrors how
        // the sibling read/write methods validate via _BuildBlobPath; without it a name like "../../etc/passwd"
        // escapes the store and leaks existence/size/timestamps (and an absolute path via _ToBlobKey).
        PathValidation.ValidatePathSegment(blobName);

        var baseDirectoryPath = Path.Combine(_basePath, container[0]).EnsureEndsWith(Path.DirectorySeparatorChar);
        var directoryPath = _GetDirectoryPath(container);
        var filePath = Path.Combine(directoryPath, blobName);

        // Defense-in-depth: verify the resolved path stays under the base directory before touching the file.
        _ThrowIfPathTraversal(filePath, nameof(blobName));

        logger.LogGettingFileStream(filePath);
        var fileInfo = new FileInfo(filePath);

        if (!fileInfo.Exists)
        {
            logger.LogFileNotFound(filePath);

            return ValueTask.FromResult<BlobInfo?>(null);
        }

        // Derive the blob key from the resolved path (stripping the container base), so the same blob yields the
        // same BlobKey here and through GetBlobsAsync / GetPagedListAsync.
        var blobKey = _ToBlobKey(fileInfo, baseDirectoryPath);

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

        // Reject traversal sequences in the search pattern (consistent with DeleteAllAsync) before it reaches
        // Directory.EnumerateFiles, so the package surfaces its own ArgumentException rather than leaning on a
        // BCL implementation detail.
        PathValidation.ValidatePathSegment(blobSearchPattern);

        blobSearchPattern = blobSearchPattern.NormalizePath();

        var baseDirectoryPath = Path.Combine(_basePath, container[0]).EnsureEndsWith(Path.DirectorySeparatorChar);
        var directoryPath = _GetDirectoryPath(container);
        var completePath = Path.GetDirectoryName(Path.Combine(directoryPath, blobSearchPattern));

        if (!Directory.Exists(completePath))
        {
            yield break;
        }

        // The directory walk below is synchronous I/O; this await keeps the method a valid async iterator.
        await ValueTask.CompletedTask.ConfigureAwait(false);

        foreach (var path in Directory.EnumerateFiles(directoryPath, blobSearchPattern, SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(path);

            if (!fileInfo.Exists)
            {
                continue;
            }

            yield return _CreateBlobInfo(fileInfo, _ToBlobKey(fileInfo, baseDirectoryPath));
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

        if (string.IsNullOrEmpty(blobSearchPattern))
        {
            blobSearchPattern = "*";
        }

        PathValidation.ValidatePathSegment(blobSearchPattern);

        blobSearchPattern = blobSearchPattern.NormalizePath();

        var baseDirectoryPath = Path.Combine(_basePath, container[0]).EnsureEndsWith(Path.DirectorySeparatorChar);
        var directoryPath = _GetDirectoryPath(container);
        var completePath = Path.GetDirectoryName(Path.Combine(directoryPath, blobSearchPattern));

        if (!Directory.Exists(completePath))
        {
            logger.LogReturningEmptyFileList(blobSearchPattern);

            return PagedFileListResult.Empty;
        }

        // Hoist a single forward-only enumerator that every page advances, instead of re-enumerating the whole
        // tree and Skip()-ping prior entries per page. This turns full enumeration from O(N^2 / pageSize) into
        // O(N); the PagedFileListResult cursor only ever moves forward, so a single enumerator is sufficient.
        var enumerator = Directory
            .EnumerateFiles(directoryPath, blobSearchPattern, SearchOption.AllDirectories)
            .GetEnumerator();

        var result = new PagedFileListResult(
            (_, _) =>
                ValueTask.FromResult<INextPageResult>(
                    _GetFiles(baseDirectoryPath, blobSearchPattern, pageSize, page: 1, enumerator, carryOver: null)
                ),
            // Deterministic cleanup: a caller that reads page 1 and stops while HasMore is still true can
            // 'await using' the result to release the find handle, instead of waiting for finalization.
            cleanup: () => enumerator.Dispose()
        );

        await result.NextPageAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    private NextPageResult _GetFiles(
        string baseDirectoryPath,
        string searchPattern,
        int pageSize,
        int page,
        IEnumerator<string> enumerator,
        BlobInfo? carryOver
    )
    {
        logger.LogGettingFileList(searchPattern, page, pageSize);

        var list = new List<BlobInfo>(pageSize);

        // The lookahead entry pulled past the previous page (to learn that this page exists) starts this page.
        if (carryOver is not null)
        {
            list.Add(carryOver);
        }

        BlobInfo? lookahead = null;
        bool hasMore;

        try
        {
            while (list.Count <= pageSize && enumerator.MoveNext())
            {
                var fileInfo = new FileInfo(enumerator.Current);

                if (!fileInfo.Exists)
                {
                    continue;
                }

                var info = _CreateBlobInfo(fileInfo, _ToBlobKey(fileInfo, baseDirectoryPath));

                // Pull one entry past the page to detect a further page, then carry it over rather than re-reading it.
                if (list.Count == pageSize)
                {
                    lookahead = info;

                    break;
                }

                list.Add(info);
            }

            hasMore = lookahead is not null;
        }
        finally
        {
            // Release the find handle when the walk reaches its end (no further page) or throws mid-iteration;
            // only an in-progress cursor with more pages keeps the enumerator alive for the next page. The
            // abandoned-cursor case is covered by PagedFileListResult.DisposeAsync via the provider cleanup delegate.
            if (lookahead is null)
            {
                enumerator.Dispose();
            }
        }

        return new NextPageResult
        {
            Success = true,
            HasMore = hasMore,
            Blobs = list,
            NextPageFunc = hasMore
                ? (_, _) =>
                    ValueTask.FromResult<INextPageResult>(
                        _GetFiles(baseDirectoryPath, searchPattern, pageSize, page + 1, enumerator, lookahead)
                    )
                : null,
        };
    }

    #endregion

    #region Helpers

    private static BlobInfo _CreateBlobInfo(FileInfo fileInfo, string blobKey) =>
        new()
        {
            BlobKey = blobKey,
            Created = new DateTimeOffset(fileInfo.CreationTimeUtc, TimeSpan.Zero),
            Modified = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
            Size = fileInfo.Length,
        };

    /// <summary>
    /// Derives the provider-relative blob key for <paramref name="fileInfo"/> by stripping the container base
    /// directory and normalizing separators to '/'. Shared by GetBlobInfoAsync, GetBlobsAsync, and GetPagedListAsync
    /// so the same physical blob yields the same key across all three.
    /// </summary>
    private static string _ToBlobKey(FileInfo fileInfo, string baseDirectoryPath) =>
        fileInfo.FullName.Replace(baseDirectoryPath, string.Empty, StringComparison.Ordinal).Replace('\\', '/');

    #endregion

    #region Build Paths

    private string _BuildBlobPath(string[] container, string blobName)
    {
        Argument.IsNotNullOrWhiteSpace(blobName);
        Argument.IsNotNullOrEmpty(container);

        PathValidation.ValidateContainer(container);
        PathValidation.ValidatePathSegment(blobName);

        var normalizedContainer = _NormalizeContainerSegments(container);
        var normalizedBlobName = normalizer.NormalizeBlobName(blobName);

        // Use single Path.Combine call to avoid intermediate string allocations
        var segments = new string[container.Length + 2];
        segments[0] = _basePath;
        normalizedContainer.CopyTo(segments, 1);
        segments[^1] = normalizedBlobName;

        var path = Path.Combine(segments);
        _ThrowIfPathTraversal(path, nameof(blobName));

        return path;
    }

    private string _GetDirectoryPath(string[] container)
    {
        Argument.IsNotNullOrEmpty(container);
        PathValidation.ValidateContainer(container);

        var filePath = Path.Combine(_basePath, Path.Combine(_NormalizeContainerSegments(container)));
        _ThrowIfPathTraversal(filePath, nameof(container));

        return filePath.EnsureEndsWith(Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Normalizes each container segment using the two-tier rule: the first segment is the top-level container
    /// name; the remaining segments are treated as path (blob) segments.
    /// </summary>
    private string[] _NormalizeContainerSegments(string[] container)
    {
        var normalized = new string[container.Length];

        for (var i = 0; i < container.Length; i++)
        {
            normalized[i] =
                i == 0 ? normalizer.NormalizeContainerName(container[i]) : normalizer.NormalizeBlobName(container[i]);
        }

        return normalized;
    }

    private void _ThrowIfPathTraversal(string path, string paramName)
    {
        // Resolve '..'/'.' segments lexically, then verify the result stays under the base directory.
        // Path.GetRelativePath honors the platform's path-casing semantics (case-insensitive on Windows,
        // case-sensitive on Linux), so the boundary check matches how the OS actually resolves the path —
        // unlike a fixed OrdinalIgnoreCase prefix compare, which is wrong on case-sensitive file systems.
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(_basePath, fullPath);

        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            // A rejected traversal attempt is a security-relevant event; surface it to logs and name the
            // offending resolved path so an operator or agent can see exactly what was blocked.
            logger.LogPathTraversalRejected(paramName, fullPath);

            throw new ArgumentException(
                $"Path traversal detected: the resolved path escapes the base directory ('{fullPath}')",
                paramName
            );
        }
    }

    #endregion

    #region Dispose

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #endregion
}
