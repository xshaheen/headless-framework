// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Framework.Blobs.Internals;
using Framework.Checks;
using Framework.Constants;
using Framework.Primitives;
using Framework.Urls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Framework.Blobs.SshNet;

public sealed class SshBlobStorage(
    SftpClientPool pool,
    IBlobNamingNormalizer normalizer,
    IOptionsMonitor<SshBlobStorageOptions> options,
    ILogger<SshBlobStorage> logger
) : IBlobStorage
{
    public async ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(container);
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogTrace("Ensuring {Directory} directory exists", (object)container);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var currentDirectory = string.Empty;

            foreach (var segment in container)
            {
                currentDirectory = string.IsNullOrEmpty(currentDirectory) ? segment : $"{currentDirectory}/{segment}";

                if (await client.ExistsAsync(currentDirectory, cancellationToken).AnyContext())
                {
                    continue;
                }

                logger.LogInformation("Creating Container segment {Segment}", segment);
                await client.CreateDirectoryAsync(currentDirectory, cancellationToken).AnyContext();
            }
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Uploads a blob to SFTP storage.
    /// </summary>
    /// <param name="container">Container path segments</param>
    /// <param name="blobName">Name of the blob to upload</param>
    /// <param name="stream">Content stream to upload</param>
    /// <param name="metadata">
    /// Optional metadata dictionary. Note: SFTP does not support blob metadata.
    /// This parameter is accepted for compatibility with the IBlobStorage interface but is ignored.
    /// </param>
    /// <param name="cancellationToken">Cancellation token</param>
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

        var blobPath = _BuildBlobPath(container, blobName);

        logger.LogTrace("Saving {Path}", blobPath);

        // Reset stream position for seekable streams
        if (stream.CanSeek && stream.Position != 0)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                await using var sftpFileStream = await client
                    .OpenAsync(blobPath, FileMode.Create, FileAccess.Write, cancellationToken)
                    .AnyContext();

                await stream.CopyToAsync(sftpFileStream, cancellationToken).AnyContext();
            }
            catch (SftpPathNotFoundException e)
            {
                logger.LogDebug(e, "Error saving {Path}: Attempting to create directory", blobPath);
                await _CreateContainerWithClientAsync(client, container, cancellationToken).AnyContext();

                await using var sftpFileStream = await client
                    .OpenAsync(blobPath, FileMode.OpenOrCreate, FileAccess.Write, cancellationToken)
                    .AnyContext();

                await stream.CopyToAsync(sftpFileStream, cancellationToken).AnyContext();
            }
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    public async ValueTask<IReadOnlyList<Result<Exception>>> BulkUploadAsync(
        string[] container,
        IReadOnlyCollection<BlobUploadRequest> blobs,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobs);
        Argument.IsNotNullOrEmpty(container);

        var results = new Result<Exception>[blobs.Count];
        var index = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.CurrentValue.MaxConcurrentOperations,
            CancellationToken = cancellationToken,
        };

        await Parallel
            .ForEachAsync(
                blobs,
                parallelOptions,
                async (blob, ct) =>
                {
                    var i = Interlocked.Increment(ref index) - 1;

                    try
                    {
                        await UploadAsync(container, blob.FileName, blob.Stream, blob.Metadata, ct).AnyContext();
                        results[i] = Result<Exception>.Ok();
                    }
                    catch (Exception e)
                    {
                        results[i] = Result<Exception>.Fail(e);
                    }
                }
            )
            .AnyContext();

        return results;
    }

    public async ValueTask<bool> DeleteAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        var blobPath = _BuildBlobPath(container, blobName);

        logger.LogTrace("Deleting {Path}", blobPath);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _DeleteWithClientAsync(client, blobPath, cancellationToken).AnyContext();
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    private async Task<bool> _DeleteWithClientAsync(
        SftpClient client,
        string blobPath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await client.DeleteFileAsync(blobPath, cancellationToken).AnyContext();
        }
        catch (SftpPathNotFoundException ex)
        {
            logger.LogError(ex, "Unable to delete {Path}: File not found", blobPath);

            return false;
        }

        return true;
    }

    public async ValueTask<IReadOnlyList<Result<bool, Exception>>> BulkDeleteAsync(
        string[] container,
        IReadOnlyCollection<string> blobNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);

        if (blobNames.Count == 0)
        {
            return [];
        }

        var results = new Result<bool, Exception>[blobNames.Count];
        var index = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.CurrentValue.MaxConcurrentOperations,
            CancellationToken = cancellationToken,
        };

        await Parallel
            .ForEachAsync(
                blobNames,
                parallelOptions,
                async (fileName, ct) =>
                {
                    var i = Interlocked.Increment(ref index) - 1;

                    try
                    {
                        results[i] = await DeleteAsync(container, fileName, ct).AnyContext();
                    }
                    catch (Exception e)
                    {
                        results[i] = Result<bool, Exception>.Fail(e);
                    }
                }
            )
            .AnyContext();

        return results;
    }

    public async ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    )
    {
        var containerPath = _BuildContainerPath(container);
        blobSearchPattern = _NormalizePath(blobSearchPattern);

        var client = await pool.AcquireAsync(cancellationToken).AnyContext();
        try
        {
            if (blobSearchPattern is null or "*")
            {
                return await _DeleteDirectoryWithClientAsync(
                        client,
                        containerPath,
                        includeSelf: false,
                        cancellationToken
                    )
                    .AnyContext();
            }

            if (blobSearchPattern.EndsWith("/*", StringComparison.Ordinal))
            {
                blobSearchPattern = containerPath + blobSearchPattern[..^2].RemovePrefix('/');

                return await _DeleteDirectoryWithClientAsync(
                        client,
                        blobSearchPattern,
                        includeSelf: false,
                        cancellationToken
                    )
                    .AnyContext();
            }

            var files = await _GetFileListWithClientAsync(
                    client,
                    container[0],
                    containerPath,
                    blobSearchPattern,
                    cancellationToken: cancellationToken
                )
                .AnyContext();

            var count = 0;

            logger.LogInformation(
                "Deleting {FileCount} files matching {SearchPattern}",
                files.Count,
                blobSearchPattern
            );

            foreach (var file in files)
            {
                var result = await _DeleteWithClientAsync(
                        client,
                        Url.Combine(container[0], file.BlobKey),
                        cancellationToken
                    )
                    .AnyContext();

                if (result)
                {
                    count++;
                }
                else
                {
                    logger.LogWarning("Failed to delete {Path}", file.BlobKey);
                }
            }

            logger.LogTrace("Finished deleting {FileCount} files matching {SearchPattern}", count, blobSearchPattern);

            return count;
        }
        finally
        {
            await pool.ReleaseAsync(client).AnyContext();
        }
    }

    public async Task<int> DeleteDirectoryAsync(
        string directory,
        bool includeSelf = true,
        CancellationToken cancellationToken = default
    )
    {
        var client = await pool.AcquireAsync(cancellationToken).AnyContext();
        try
        {
            return await _DeleteDirectoryWithClientAsync(client, directory, includeSelf, cancellationToken)
                .AnyContext();
        }
        finally
        {
            await pool.ReleaseAsync(client).AnyContext();
        }
    }

    private async Task<int> _DeleteDirectoryWithClientAsync(
        SftpClient client,
        string directory,
        bool includeSelf,
        CancellationToken cancellationToken
    )
    {
        logger.LogInformation("Deleting {Directory} directory", directory);

        var filesToDelete = new List<string>();
        var dirsToDelete = new List<string>();

        try
        {
            // Collect all paths recursively
            await _CollectPathsRecursively(client, directory, filesToDelete, dirsToDelete, cancellationToken)
                .AnyContext();

            var count = 0;

            // Parallel file deletion
            if (filesToDelete.Count > 0)
            {
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.CurrentValue.MaxConcurrentOperations,
                    CancellationToken = cancellationToken,
                };

                await Parallel
                    .ForEachAsync(
                        filesToDelete,
                        parallelOptions,
                        async (path, ct) =>
                        {
                            logger.LogTrace("Deleting file {Path}", path);
                            await client.DeleteFileAsync(path, ct).AnyContext();
                            Interlocked.Increment(ref count);
                        }
                    )
                    .AnyContext();
            }

            // Sequential directory deletion (must be bottom-up)
            foreach (var dir in dirsToDelete.OrderByDescending(d => d.Length))
            {
                await client.DeleteDirectoryAsync(dir, cancellationToken).AnyContext();
            }

            if (includeSelf)
            {
                await client.DeleteDirectoryAsync(directory, cancellationToken).AnyContext();
            }

            logger.LogTrace("Finished deleting {Directory} directory with {FileCount} files", directory, count);

            return count;
        }
        catch (SftpPathNotFoundException)
        {
            logger.LogTrace("Delete directory not found with {Directory}", directory);
            return 0;
        }
    }

    private async Task _CollectPathsRecursively(
        SftpClient client,
        string directory,
        List<string> filesToDelete,
        List<string> dirsToDelete,
        CancellationToken cancellationToken
    )
    {
        await foreach (var file in client.ListDirectoryAsync(directory, cancellationToken).AnyContext())
        {
            if (file.Name is "." or "..")
            {
                continue;
            }

            if (file.IsDirectory)
            {
                dirsToDelete.Add(file.FullName);
                await _CollectPathsRecursively(client, file.FullName, filesToDelete, dirsToDelete, cancellationToken)
                    .AnyContext();
            }
            else
            {
                filesToDelete.Add(file.FullName);
            }
        }
    }

    public async ValueTask<bool> RenameAsync(
        string[] blobContainer,
        string blobName,
        string[] newBlobContainer,
        string newBlobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(blobContainer);
        Argument.IsNotNull(newBlobName);
        Argument.IsNotNull(newBlobContainer);

        var blobPath = _BuildBlobPath(blobContainer, blobName);
        var targetPath = _BuildBlobPath(newBlobContainer, newBlobName);

        logger.LogInformation("Renaming {Path} to {TargetPath}", blobPath, targetPath);

        var client = await pool.AcquireAsync(cancellationToken).AnyContext();
        try
        {
            // If the target path already exists, delete it.
            if (await client.ExistsAsync(targetPath, cancellationToken).AnyContext())
            {
                logger.LogDebug("Removing existing {TargetPath} path for rename operation", targetPath);
                await _DeleteWithClientAsync(client, targetPath, cancellationToken).AnyContext();
                logger.LogDebug("Removed existing {TargetPath} path for rename operation", targetPath);
            }

            try
            {
                await client.RenameFileAsync(blobPath, targetPath, cancellationToken).AnyContext();
            }
            catch (SftpPathNotFoundException e)
            {
                logger.LogDebug(
                    e,
                    "Error renaming {Path} to {NewPath}: Attempting to create directory",
                    blobPath,
                    targetPath
                );

                try
                {
                    await _CreateContainerWithClientAsync(client, newBlobContainer, cancellationToken).AnyContext();
                    await client.RenameFileAsync(blobPath, targetPath, cancellationToken).AnyContext();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error renaming {Path} to {NewPath}", blobPath, targetPath);

                    return false;
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error renaming {Path} to {NewPath}", blobPath, targetPath);

                return false;
            }

            return true;
        }
        finally
        {
            await pool.ReleaseAsync(client).AnyContext();
        }
    }

    public async ValueTask<bool> CopyAsync(
        string[] blobContainer,
        string blobName,
        string[] newBlobContainer,
        string newBlobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(blobContainer);
        Argument.IsNotNull(newBlobName);
        Argument.IsNotNull(newBlobContainer);

        // Validate paths before try-catch to ensure security exceptions propagate
        PathValidation.ValidatePathSegment(blobName);
        PathValidation.ValidatePathSegment(newBlobName);
        PathValidation.ValidateContainer(blobContainer);
        PathValidation.ValidateContainer(newBlobContainer);

        var sourcePath = _BuildBlobPath(blobContainer, blobName);
        var destPath = _BuildBlobPath(newBlobContainer, newBlobName);

        logger.LogInformation(
            "Copying {@Container}/{Path} to {@TargetContainer}/{TargetPath}",
            blobContainer,
            blobName,
            newBlobContainer,
            newBlobName
        );

        var client = await pool.AcquireAsync(cancellationToken).AnyContext();
        try
        {
            // Open source stream for reading
            await using var sourceStream = await client
                .OpenAsync(sourcePath, FileMode.Open, FileAccess.Read, cancellationToken)
                .AnyContext();

            // Ensure destination container exists
            await _CreateContainerWithClientAsync(client, newBlobContainer, cancellationToken).AnyContext();

            // Open destination stream for writing
            await using var destStream = await client
                .OpenAsync(destPath, FileMode.Create, FileAccess.Write, cancellationToken)
                .AnyContext();

            // Stream copy with 80KB buffer - constant memory usage regardless of file size
            await sourceStream.CopyToAsync(destStream, 81920, cancellationToken).AnyContext();

            return true;
        }
        catch (SftpPathNotFoundException ex)
        {
            logger.LogError(ex, "Source file not found: {@Container}/{Path}", blobContainer, blobName);

            return false;
        }
        catch (Exception e)
        {
            logger.LogError(
                e,
                "Error copying {@Container}/{Path} to {@TargetContainer}/{TargetPath}",
                blobContainer,
                blobName,
                newBlobContainer,
                newBlobName
            );

            return false;
        }
        finally
        {
            await pool.ReleaseAsync(client).AnyContext();
        }
    }

    public async ValueTask<bool> ExistsAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        var blobPath = _BuildBlobPath(container, blobName);

        logger.LogTrace("Checking if {Path} exists", blobPath);

        var client = await pool.AcquireAsync(cancellationToken).AnyContext();
        try
        {
            return await client.ExistsAsync(blobPath, cancellationToken).AnyContext();
        }
        finally
        {
            await pool.ReleaseAsync(client).AnyContext();
        }
    }

    /// <inheritdoc />
    public async ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        var blobPath = _BuildBlobPath(container, blobName);

        logger.LogTrace("Getting file stream for {Path}", blobPath);

        var client = await pool.AcquireAsync(cancellationToken).AnyContext();

        try
        {
            var sftpStream = await client
                .OpenAsync(blobPath, FileMode.Open, FileAccess.Read, cancellationToken)
                .AnyContext();

            var wrappedStream = new PooledClientStream(sftpStream, client, pool);

#pragma warning disable CA2000 // Dispose objects before losing scope - ownership transferred to caller
            return new BlobDownloadResult(wrappedStream, blobName);
#pragma warning restore CA2000
        }
        catch (SftpPathNotFoundException ex)
        {
            logger.LogError(ex, "Unable to get file stream for {Path}: File Not Found", blobPath);
            await pool.ReleaseAsync(client).AnyContext();

            return null;
        }
        catch
        {
            await pool.ReleaseAsync(client).AnyContext();
            throw;
        }
    }

    public async ValueTask<BlobInfo?> GetBlobInfoAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);
        Argument.IsNotNull(blobName);

        var directoryPath = _BuildContainerPath(container);
        var blobPath = directoryPath + blobName;
        logger.LogTrace("Getting blob info for {Path}", blobPath);

        var client = await pool.AcquireAsync(cancellationToken).AnyContext();
        try
        {
            var file = await client.GetAsync(blobPath, cancellationToken).AnyContext();

            if (file.IsDirectory)
            {
                logger.LogWarning("Unable to get blob info for {Path}: Is a directory", blobPath);

                return null;
            }

            var objectKey = blobPath.Replace(container[0], string.Empty, StringComparison.Ordinal).TrimStart('/');

            return _ToBlobInfo(file, objectKey);
        }
        catch (SftpPathNotFoundException ex)
        {
            logger.LogError(ex, "Unable to get file info for {Path}: File Not Found", blobPath);

            return null;
        }
        finally
        {
            await pool.ReleaseAsync(client).AnyContext();
        }
    }

    public async IAsyncEnumerable<BlobInfo> GetBlobsAsync(
        string[] container,
        string? blobSearchPattern = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);

        var directoryPath = _BuildContainerPath(container);
        var criteria = _GetRequestCriteria(directoryPath, blobSearchPattern);

        logger.LogTrace(
            "Getting blobs recursively matching {Prefix} and {Pattern}...",
            criteria.PathPrefix,
            criteria.Pattern
        );

        var client = await pool.AcquireAsync(cancellationToken).AnyContext();
        try
        {
            await foreach (
                var blob in _GetBlobsRecursivelyAsync(
                        client,
                        container[0],
                        criteria.PathPrefix,
                        criteria.Pattern,
                        cancellationToken
                    )
                    .AnyContext()
            )
            {
                yield return blob;
            }
        }
        finally
        {
            await pool.ReleaseAsync(client).AnyContext();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// WARNING: SFTP does not support server-side pagination. Each page request
    /// enumerates from the beginning. For large directories, use GetBlobsAsync()
    /// with client-side pagination instead.
    /// </remarks>
    public async ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] container,
        string? blobSearchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);
        Argument.IsPositive(pageSize);
        Argument.IsLessThanOrEqualTo(pageSize, int.MaxValue - 1);

        var directoryPath = _BuildContainerPath(container);

        var result = new PagedFileListResult(
            (_, token) => _GetFilesAsync(container[0], directoryPath, blobSearchPattern, 1, pageSize, token)
        );

        await result.NextPageAsync(cancellationToken).AnyContext();

        return result;
    }

    private async ValueTask<INextPageResult> _GetFilesAsync(
        string baseContainer,
        string directoryPath,
        string? searchPattern,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        var pagingLimit = pageSize;
        var skip = (page - 1) * pagingLimit;

        if (pagingLimit < int.MaxValue)
        {
            pagingLimit++;
        }

        var client = await pool.AcquireAsync(cancellationToken).AnyContext();
        try
        {
            var list = await _GetFileListWithClientAsync(
                    client,
                    baseContainer,
                    directoryPath,
                    searchPattern,
                    pagingLimit,
                    skip,
                    cancellationToken
                )
                .AnyContext();
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
                    ? (_, token) =>
                        _GetFilesAsync(baseContainer, directoryPath, searchPattern, page + 1, pageSize, token)
                    : null,
            };
        }
        finally
        {
            await pool.ReleaseAsync(client).AnyContext();
        }
    }

    private async Task<List<BlobInfo>> _GetFileListWithClientAsync(
        SftpClient client,
        string baseContainer,
        string directoryPath,
        string? searchPattern = null,
        int? limit = null,
        int? skip = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(limit);
        Argument.IsPositiveOrZero(skip);

        var criteria = _GetRequestCriteria(directoryPath, searchPattern);

        logger.LogTrace(
            "Getting file list recursively matching {Prefix} and {Pattern}...",
            criteria.PathPrefix,
            criteria.Pattern
        );

        var items = new List<BlobInfo>();
        var count = 0;
        var recordsToReturn = limit.HasValue ? (skip.GetValueOrDefault() + limit.Value) : (int?)null;

        await foreach (
            var blob in _GetBlobsRecursivelyAsync(
                    client,
                    baseContainer,
                    criteria.PathPrefix,
                    criteria.Pattern,
                    cancellationToken
                )
                .AnyContext()
        )
        {
            if (recordsToReturn.HasValue && count >= recordsToReturn)
            {
                break;
            }

            items.Add(blob);
            count++;
        }

        if (skip is null && limit is null)
        {
            return items;
        }

        IEnumerable<BlobInfo> page = items;

        if (skip.HasValue)
        {
            page = page.Skip(skip.Value);
        }

        if (limit.HasValue)
        {
            page = page.Take(limit.Value);
        }

        return page.ToList();
    }

    private static async IAsyncEnumerable<ISftpFile> _SafeListDirectoryAsync(
        IAsyncEnumerable<ISftpFile> files,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var enumerator = files.GetAsyncEnumerator(cancellationToken);
        await using (enumerator)
        {
            while (true)
            {
                bool hasNext;

                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (SftpPathNotFoundException)
                {
                    // Directory was deleted during iteration - stop enumeration
                    yield break;
                }

                if (!hasNext)
                {
                    break;
                }

                yield return enumerator.Current;
            }
        }
    }

    private async IAsyncEnumerable<BlobInfo> _GetBlobsRecursivelyAsync(
        SftpClient client,
        string baseContainer,
        string currentPathPrefix,
        Regex? pattern,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(currentPathPrefix);

        if (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Cancellation requested");
            yield break;
        }

        IAsyncEnumerable<ISftpFile> files;

        try
        {
            files = client.ListDirectoryAsync(currentPathPrefix, cancellationToken);
        }
        catch (SftpPathNotFoundException)
        {
            logger.LogDebug("Directory not found with {PathPrefix}", currentPathPrefix);
            yield break;
        }

        await foreach (var file in _SafeListDirectoryAsync(files, cancellationToken).AnyContext())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Cancellation requested");
                yield break;
            }

            if (file is { IsDirectory: true, Name: "." or ".." })
            {
                continue;
            }

            if (file is { IsRegularFile: false, IsDirectory: false })
            {
                continue;
            }

            var path = string.IsNullOrEmpty(currentPathPrefix) ? file.Name : $"{currentPathPrefix}{file.Name}";

            if (file.IsDirectory)
            {
                path += "/";

                await foreach (
                    var blob in _GetBlobsRecursivelyAsync(client, baseContainer, path, pattern, cancellationToken)
                        .AnyContext()
                )
                {
                    yield return blob;
                }

                continue;
            }

            if (!file.IsRegularFile)
            {
                continue;
            }

            if (pattern?.IsMatch(path) == false)
            {
                logger.LogTrace("Skipping {Path}: Doesn't match pattern", path);
                continue;
            }

            var objectKey = path.Replace(baseContainer, string.Empty, StringComparison.Ordinal).TrimStart('/');
            yield return _ToBlobInfo(file, objectKey);
        }
    }

    private async Task _CreateContainerWithClientAsync(
        SftpClient client,
        string[] container,
        CancellationToken cancellationToken
    )
    {
        var currentDirectory = string.Empty;

        foreach (var segment in container)
        {
            currentDirectory = string.IsNullOrEmpty(currentDirectory) ? segment : $"{currentDirectory}/{segment}";

            if (await client.ExistsAsync(currentDirectory, cancellationToken).AnyContext())
            {
                continue;
            }

            logger.LogInformation("Creating Container segment {Segment}", segment);
            await client.CreateDirectoryAsync(currentDirectory, cancellationToken).AnyContext();
        }
    }

    private static SearchCriteria _GetRequestCriteria(string directoryPath, string? searchPattern)
    {
        searchPattern = searchPattern?.TrimStart('/').TrimStart('\\');

        if (string.IsNullOrEmpty(searchPattern))
        {
            return new(directoryPath);
        }

        searchPattern = _NormalizePath($"{directoryPath}{searchPattern}");
        var wildcardPos = searchPattern.IndexOf('*', StringComparison.Ordinal);
        var hasWildcard = wildcardPos >= 0;

        string prefix;
        Regex patternRegex;

        if (hasWildcard)
        {
            var searchRegexText = Regex.Escape(searchPattern).Replace("\\*", ".*?", StringComparison.Ordinal);
            patternRegex = new Regex($"^{searchRegexText}$", RegexOptions.ExplicitCapture, RegexPatterns.MatchTimeout);
            var beforeWildcard = searchPattern[..wildcardPos];
            var slashPos = beforeWildcard.LastIndexOf('/');
            prefix = slashPos >= 0 ? searchPattern[..(slashPos + 1)] : string.Empty;
        }
        else
        {
            patternRegex = new Regex($"^{searchPattern}$", RegexOptions.ExplicitCapture, RegexPatterns.MatchTimeout);
            var slashPos = searchPattern.LastIndexOf('/');
            prefix = slashPos >= 0 ? searchPattern[..(slashPos + 1)] : string.Empty;
        }

        return new(prefix, patternRegex);
    }

    private sealed record SearchCriteria(string PathPrefix = "", Regex? Pattern = null);

    private string _BuildBlobPath(string[] container, string blobName)
    {
        PathValidation.ValidatePathSegment(blobName);
        PathValidation.ValidateContainer(container);

        var normalizedBlobName = normalizer.NormalizeBlobName(blobName);

        if (container.Length == 0)
        {
            return normalizedBlobName;
        }

        var sb = new StringBuilder();

        foreach (var segment in container)
        {
            if (sb.Length > 0)
            {
                sb.Append('/');
            }

            sb.Append(normalizer.NormalizeContainerName(segment));
        }

        if (!string.IsNullOrEmpty(normalizedBlobName))
        {
            sb.Append('/');
        }

        sb.Append(normalizedBlobName);

        return sb.ToString();
    }

    private string _BuildContainerPath(string[] container)
    {
        if (container.Length == 0)
        {
            return "";
        }

        PathValidation.ValidateContainer(container);

        var normalizedSegments = new string[container.Length];
        for (var i = 0; i < container.Length; i++)
        {
            normalizedSegments[i] = normalizer.NormalizeContainerName(container[i]);
        }

        return $"{string.Join('/', normalizedSegments)}/";
    }

    [return: NotNullIfNotNull(nameof(path))]
    private static string? _NormalizePath(string? path)
    {
        return path?.Replace('\\', '/');
    }

    private static BlobInfo _ToBlobInfo(ISftpFile file, string objectKey)
    {
        var modified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);

        return new BlobInfo
        {
            BlobKey = objectKey,
            // SFTP doesn't provide creation time, so we use modified time.
            Created = modified,
            Modified = modified,
            Size = file.Length,
        };
    }

    public ValueTask DisposeAsync()
    {
        // Pool is managed by DI container - don't dispose here
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Wrapper stream that releases the SFTP client back to the pool when disposed.
/// Allows the caller to own the stream lifetime while properly managing the pooled client.
/// </summary>
file sealed class PooledClientStream(Stream innerStream, SftpClient client, SftpClientPool pool) : Stream
{
    private bool _disposed;

    public override bool CanRead => innerStream.CanRead;
    public override bool CanSeek => innerStream.CanSeek;
    public override bool CanWrite => innerStream.CanWrite;
    public override long Length => innerStream.Length;

    public override long Position
    {
        get => innerStream.Position;
        set => innerStream.Position = value;
    }

    public override void Flush() => innerStream.Flush();

    public override int Read(byte[] buffer, int offset, int count) => innerStream.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => innerStream.Seek(offset, origin);

    public override void SetLength(long value) => innerStream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) => innerStream.Write(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        innerStream.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        innerStream.ReadAsync(buffer, cancellationToken);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        innerStream.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    ) => innerStream.WriteAsync(buffer, cancellationToken);

    public override Task FlushAsync(CancellationToken cancellationToken) => innerStream.FlushAsync(cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            innerStream.Dispose();
            pool.ReleaseAsync(client).AsTask().GetAwaiter().GetResult();
            _disposed = true;
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await innerStream.DisposeAsync().ConfigureAwait(false);
            await pool.ReleaseAsync(client).ConfigureAwait(false);
            _disposed = true;
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }
}
