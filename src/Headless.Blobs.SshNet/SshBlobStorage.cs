// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Headless.Blobs.Internals;
using Headless.Checks;
using Headless.Constants;
using Headless.Primitives;
using Headless.Urls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Headless.Blobs.SshNet;

/// <summary>
/// <see cref="IBlobStorage"/> implementation backed by an SFTP server via the Renci SSH.NET library.
/// </summary>
/// <remarks>
/// Connections are managed by an internal <see cref="SftpClientPool"/>. SFTP does not support native server-side
/// metadata or pagination; both features are emulated in the client. In particular, <c>GetPagedListAsync</c>
/// re-enumerates from the start for every page, making full enumeration O(n²); prefer <c>GetBlobsAsync</c>
/// for large directories.
/// <para>
/// Metadata supplied on upload is accepted for interface compatibility but is silently discarded — SFTP has no
/// per-file metadata concept.
/// </para>
/// </remarks>
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
        logger.LogEnsuringDirectoryExists(container);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var currentDirectory = string.Empty;

            foreach (var segment in container)
            {
                currentDirectory = string.IsNullOrEmpty(currentDirectory) ? segment : $"{currentDirectory}/{segment}";

                if (await client.ExistsAsync(currentDirectory, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                logger.LogCreatingContainerSegment(segment);
                await client.CreateDirectoryAsync(currentDirectory, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Uploads <paramref name="stream"/> as a blob named <paramref name="blobName"/> to the SFTP server.
    /// </summary>
    /// <param name="container">Hierarchical path segments identifying the target directory on the SFTP server.</param>
    /// <param name="blobName">The file name to create or overwrite on the server.</param>
    /// <param name="stream">Content stream to upload. Seekable streams are rewound to position 0 before upload.</param>
    /// <param name="metadata">
    /// Accepted for interface compatibility but silently ignored — SFTP has no per-file metadata concept.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="blobName"/> or <paramref name="container"/> is null or empty.</exception>
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

        logger.LogSavingBlob(blobPath);

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
                    .ConfigureAwait(false);

                await stream.CopyToAsync(sftpFileStream, cancellationToken).ConfigureAwait(false);
            }
            catch (SftpPathNotFoundException e)
            {
                logger.LogErrorSavingBlobCreatingDirectory(e, blobPath);
                await _CreateContainerWithClientAsync(client, container, cancellationToken).ConfigureAwait(false);

                await using var sftpFileStream = await client
                    .OpenAsync(blobPath, FileMode.OpenOrCreate, FileAccess.Write, cancellationToken)
                    .ConfigureAwait(false);

                await stream.CopyToAsync(sftpFileStream, cancellationToken).ConfigureAwait(false);
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

        // Index results by input position, not execution-start order: Parallel.ForEachAsync does not run bodies in
        // enumeration order, so an Interlocked counter would misalign results with their inputs. Honors the
        // "one Result per input blob, in original order" contract.
        var items = blobs as IReadOnlyList<BlobUploadRequest> ?? blobs.ToList();
        var results = new Result<Exception>[items.Count];

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.CurrentValue.MaxConcurrentOperations,
            CancellationToken = cancellationToken,
        };

        await Parallel
            .ForAsync(
                0,
                items.Count,
                parallelOptions,
                async (i, ct) =>
                {
                    var blob = items[i];

                    try
                    {
                        await UploadAsync(container, blob.FileName, blob.Stream, blob.Metadata, ct)
                            .ConfigureAwait(false);
                        results[i] = Result<Exception>.Ok();
                    }
                    catch (Exception e)
                    {
                        results[i] = Result<Exception>.Fail(e);
                    }
                }
            )
            .ConfigureAwait(false);

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

        logger.LogDeletingBlob(blobPath);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _DeleteWithClientAsync(client, blobPath, cancellationToken).ConfigureAwait(false);
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
            await client.DeleteFileAsync(blobPath, cancellationToken).ConfigureAwait(false);
        }
        catch (SftpPathNotFoundException ex)
        {
            logger.LogDeleteFileNotFound(ex, blobPath);

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

        // Index results by input position (see BulkUploadAsync) so each entry matches its blob name in original order.
        var names = blobNames as IReadOnlyList<string> ?? blobNames.ToList();
        var results = new Result<bool, Exception>[names.Count];

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.CurrentValue.MaxConcurrentOperations,
            CancellationToken = cancellationToken,
        };

        await Parallel
            .ForAsync(
                0,
                names.Count,
                parallelOptions,
                async (i, ct) =>
                {
                    try
                    {
                        results[i] = await DeleteAsync(container, names[i], ct).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        results[i] = Result<bool, Exception>.Fail(e);
                    }
                }
            )
            .ConfigureAwait(false);

        return results;
    }

    public async ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    )
    {
        // Guard before building the path: _BuildContainerPath returns "" for an empty container, which would
        // otherwise target the connection root and (with a null/"*" pattern) delete everything under it.
        Argument.IsNotNullOrEmpty(container);

        var containerPath = _BuildContainerPath(container);
        blobSearchPattern = BlobStorageHelpers.NormalizePath(blobSearchPattern);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
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
                    .ConfigureAwait(false);
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
                    .ConfigureAwait(false);
            }

            var files = await _GetFileListWithClientAsync(
                    client,
                    container[0],
                    containerPath,
                    blobSearchPattern,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            var count = 0;

            logger.LogDeletingFilesMatchingPattern(files.Count, blobSearchPattern);

            foreach (var file in files)
            {
                var result = await _DeleteWithClientAsync(
                        client,
                        Url.Combine(container[0], file.BlobKey),
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                if (result)
                {
                    count++;
                }
                else
                {
                    logger.LogFailedToDeleteFile(file.BlobKey);
                }
            }

            logger.LogFinishedDeletingFilesMatchingPattern(count, blobSearchPattern);

            return count;
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes all files and subdirectories inside <paramref name="directory"/> on the SFTP server, optionally
    /// removing the directory itself.
    /// </summary>
    /// <param name="directory">Absolute SFTP path of the directory to delete.</param>
    /// <param name="includeSelf">
    /// When <see langword="true"/> (the default), the directory itself is removed after its contents; when
    /// <see langword="false"/>, only the contents are deleted and the directory is left empty.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of files deleted. Subdirectories are not counted.</returns>
    /// <remarks>
    /// Files are deleted in parallel up to <see cref="SshBlobStorageOptions.MaxConcurrentOperations"/>.
    /// Subdirectories are then deleted sequentially from deepest to shallowest. If the directory does not exist,
    /// returns 0 without throwing.
    /// </remarks>
    public async Task<int> DeleteDirectoryAsync(
        string directory,
        bool includeSelf = true,
        CancellationToken cancellationToken = default
    )
    {
        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _DeleteDirectoryWithClientAsync(client, directory, includeSelf, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    private async Task<int> _DeleteDirectoryWithClientAsync(
        SftpClient client,
        string directory,
        bool includeSelf,
        CancellationToken cancellationToken
    )
    {
        logger.LogDeletingDirectory(directory);

        var filesToDelete = new List<string>();
        var dirsToDelete = new List<string>();

        try
        {
            // Collect all paths recursively
            await _CollectPathsRecursively(client, directory, filesToDelete, dirsToDelete, cancellationToken)
                .ConfigureAwait(false);

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
                            logger.LogDeletingFile(path);
                            await client.DeleteFileAsync(path, ct).ConfigureAwait(false);
                            Interlocked.Increment(ref count);
                        }
                    )
                    .ConfigureAwait(false);
            }

            // Sequential directory deletion (must be bottom-up)
            foreach (var dir in dirsToDelete.OrderByDescending(d => d.Length))
            {
                await client.DeleteDirectoryAsync(dir, cancellationToken).ConfigureAwait(false);
            }

            if (includeSelf)
            {
                await client.DeleteDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
            }

            logger.LogFinishedDeletingDirectory(directory, count);

            return count;
        }
        catch (SftpPathNotFoundException)
        {
            logger.LogDeleteDirectoryNotFound(directory);
            return 0;
        }
    }

    private static async Task _CollectPathsRecursively(
        SftpClient client,
        string directory,
        List<string> filesToDelete,
        List<string> dirsToDelete,
        CancellationToken cancellationToken
    )
    {
        await foreach (var file in client.ListDirectoryAsync(directory, cancellationToken).ConfigureAwait(false))
        {
            if (file.Name is "." or "..")
            {
                continue;
            }

            if (file.IsDirectory)
            {
                dirsToDelete.Add(file.FullName);
                await _CollectPathsRecursively(client, file.FullName, filesToDelete, dirsToDelete, cancellationToken)
                    .ConfigureAwait(false);
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

        logger.LogRenamingBlob(blobPath, targetPath);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // If the target path already exists, delete it.
            if (await client.ExistsAsync(targetPath, cancellationToken).ConfigureAwait(false))
            {
                logger.LogRemovingExistingForRename(targetPath);
                await _DeleteWithClientAsync(client, targetPath, cancellationToken).ConfigureAwait(false);
                logger.LogRemovedExistingForRename(targetPath);
            }

            try
            {
                await client.RenameFileAsync(blobPath, targetPath, cancellationToken).ConfigureAwait(false);
            }
            catch (SftpPathNotFoundException e)
            {
                logger.LogErrorRenamingBlobCreatingDirectory(e, blobPath, targetPath);

                try
                {
                    await _CreateContainerWithClientAsync(client, newBlobContainer, cancellationToken)
                        .ConfigureAwait(false);
                    await client.RenameFileAsync(blobPath, targetPath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogErrorRenamingBlob(ex, blobPath, targetPath);

                    return false;
                }
            }
            catch (Exception e)
            {
                logger.LogErrorRenamingBlob(e, blobPath, targetPath);

                return false;
            }

            return true;
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
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

        logger.LogCopyingBlob(blobContainer, blobName, newBlobContainer, newBlobName);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Open source stream for reading
            await using var sourceStream = await client
                .OpenAsync(sourcePath, FileMode.Open, FileAccess.Read, cancellationToken)
                .ConfigureAwait(false);

            // Ensure destination container exists
            await _CreateContainerWithClientAsync(client, newBlobContainer, cancellationToken).ConfigureAwait(false);

            // Open destination stream for writing
            await using var destStream = await client
                .OpenAsync(destPath, FileMode.Create, FileAccess.Write, cancellationToken)
                .ConfigureAwait(false);

            // Stream copy with 80KB buffer - constant memory usage regardless of file size
            await sourceStream.CopyToAsync(destStream, 81920, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (SftpPathNotFoundException ex)
        {
            logger.LogCopySourceNotFound(ex, blobContainer, blobName);

            return false;
        }
        catch (Exception e)
        {
            logger.LogErrorCopyingBlob(e, blobContainer, blobName, newBlobContainer, newBlobName);

            return false;
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
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

        logger.LogCheckingBlobExists(blobPath);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await client.ExistsAsync(blobPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
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

        logger.LogGettingFileStream(blobPath);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var sftpStream = await client
                .OpenAsync(blobPath, FileMode.Open, FileAccess.Read, cancellationToken)
                .ConfigureAwait(false);

            var wrappedStream = new PooledClientStream(sftpStream, client, pool);

#pragma warning disable CA2000 // Dispose objects before losing scope - ownership transferred to caller
            return new BlobDownloadResult(wrappedStream, blobName);
#pragma warning restore CA2000
        }
        catch (SftpPathNotFoundException ex)
        {
            logger.LogFileStreamNotFound(ex, blobPath);
            await pool.ReleaseAsync(client).ConfigureAwait(false);

            return null;
        }
        catch
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
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
        logger.LogGettingBlobInfo(blobPath);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await client.GetAsync(blobPath, cancellationToken).ConfigureAwait(false);

            if (file.IsDirectory)
            {
                logger.LogBlobInfoIsDirectory(blobPath);

                return null;
            }

            var objectKey = blobPath.Replace(container[0], string.Empty, StringComparison.Ordinal).TrimStart('/');

            return _ToBlobInfo(file, objectKey);
        }
        catch (SftpPathNotFoundException ex)
        {
            logger.LogBlobInfoNotFound(ex, blobPath);

            return null;
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
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

        logger.LogGettingBlobsRecursively(criteria.PathPrefix, criteria.Pattern);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
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
                    .ConfigureAwait(false)
            )
            {
                yield return blob;
            }
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// SFTP does not support server-side pagination. Each page request re-enumerates the remote directory tree
    /// from the beginning, resulting in O(n²) I/O cost for full enumeration. For large directories, use
    /// <see cref="GetBlobsAsync"/> with client-side pagination instead.
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

        await result.NextPageAsync(cancellationToken).ConfigureAwait(false);

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

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
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
                .ConfigureAwait(false);
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
            await pool.ReleaseAsync(client).ConfigureAwait(false);
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

        logger.LogGettingFileListRecursively(criteria.PathPrefix, criteria.Pattern);

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
                .ConfigureAwait(false)
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
            logger.LogCancellationRequested();
            yield break;
        }

        IAsyncEnumerable<ISftpFile> files;

        try
        {
            files = client.ListDirectoryAsync(currentPathPrefix, cancellationToken);
        }
        catch (SftpPathNotFoundException)
        {
            logger.LogDirectoryNotFound(currentPathPrefix);
            yield break;
        }

        await foreach (var file in _SafeListDirectoryAsync(files, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogCancellationRequested();
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
                        .ConfigureAwait(false)
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
                logger.LogSkippingPathNoMatch(path);
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

            if (await client.ExistsAsync(currentDirectory, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            logger.LogCreatingContainerSegment(segment);
            await client.CreateDirectoryAsync(currentDirectory, cancellationToken).ConfigureAwait(false);
        }
    }

    private static SearchCriteria _GetRequestCriteria(string directoryPath, string? searchPattern)
    {
        searchPattern = searchPattern?.TrimStart('/').TrimStart('\\');

        if (string.IsNullOrEmpty(searchPattern))
        {
            return new(directoryPath);
        }

        searchPattern = BlobStorageHelpers.NormalizePath($"{directoryPath}{searchPattern}");
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

        for (var i = 0; i < container.Length; i++)
        {
            if (sb.Length > 0)
            {
                sb.Append('/');
            }

            // Two-tier: the first segment is the top-level container; the rest are path segments.
            sb.Append(
                i == 0 ? normalizer.NormalizeContainerName(container[i]) : normalizer.NormalizeBlobName(container[i])
            );
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
            // Two-tier: the first segment is the top-level container; the rest are path segments.
            normalizedSegments[i] =
                i == 0 ? normalizer.NormalizeContainerName(container[i]) : normalizer.NormalizeBlobName(container[i]);
        }

        return $"{string.Join('/', normalizedSegments)}/";
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

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        innerStream.WriteAsync(buffer, cancellationToken);

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
