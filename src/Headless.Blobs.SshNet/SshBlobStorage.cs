// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Blobs.Internals;
using Headless.Checks;
using Headless.Primitives;
using Headless.Serializer;
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
/// <para>
/// Connections are managed by an internal <see cref="SftpClientPool"/> that the DI container owns; this engine never
/// disposes it. Every operation routes its address through <see cref="BlobLocationResolver"/>, so path security
/// (traversal, control characters, absolute paths, reserved sidecar suffix) is enforced once at
/// <see cref="BlobLocation"/>/<see cref="BlobQuery"/> construction and provider normalization is applied in a single
/// seam — no method re-implements key building (folds H3).
/// </para>
/// <para>
/// SFTP has no native per-file metadata, so metadata is stored in a companion "sidecar" file next to the blob
/// (<c>"&lt;blobpath&gt;" + <see cref="BlobStorageHelpers.SidecarSuffix"/></c>). Writes are content-first, then the
/// sidecar; a missing sidecar reads as no metadata. Reading metadata or info costs an extra round trip
/// (acknowledged). Sidecars are filtered from <see cref="ListAsync"/> and are deleted/moved/copied alongside their
/// blob so re-uploading a key without metadata cannot resurrect stale metadata.
/// </para>
/// <para>
/// SFTP supports no server-side pagination: <see cref="ListAsync"/> recursively enumerates, sorts by key, and encodes
/// a lexicographic <i>start-after-key</i> continuation token. Full enumeration re-scans per page (an emulated tier,
/// weaker stability than S3/Azure native tokens). Non-seekable upload streams are passed through to the SFTP write
/// stream without buffering; seekable streams are rewound to position 0 first.
/// </para>
/// </remarks>
public sealed class SshBlobStorage(
    SftpClientPool pool,
    IBlobNamingNormalizer normalizer,
    IJsonSerializer serializer,
    IOptionsMonitor<SshBlobStorageOptions> options,
    TimeProvider timeProvider,
    ILogger<SshBlobStorage> logger
) : IBlobStorage
{
    #region Upload

    public async ValueTask UploadAsync(
        BlobLocation location,
        Stream content,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(content);

        var (blobPath, sidecarPath) = _ResolvePaths(location);

        logger.LogSavingBlob(blobPath);

        // Rewind seekable streams; non-seekable streams pass through to the SFTP write stream unbuffered.
        if (content.CanSeek && content.Position != 0)
        {
            content.Seek(0, SeekOrigin.Begin);
        }

        var payload = _BuildSidecarPayload(metadata, location.Path);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Write content first, creating only container-relative parent directories on demand. The top-level
            // container must already exist because explicit container lifecycle lives on IBlobContainerManager.
            try
            {
                await _WriteAllAsync(client, blobPath, content, cancellationToken).ConfigureAwait(false);
            }
            catch (SftpPathNotFoundException e)
            {
                logger.LogErrorSavingBlobCreatingDirectory(e, blobPath);
                await _EnsureParentDirectoryAsync(client, blobPath, cancellationToken).ConfigureAwait(false);
                await _WriteAllAsync(client, blobPath, content, cancellationToken).ConfigureAwait(false);
            }

            // Sidecar second, so a crash between the two leaves the blob readable with no metadata rather than the
            // reverse. The parent directory already exists after the content write.
            await _WriteSidecarAsync(client, sidecarPath, payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    public async ValueTask<IReadOnlyList<BlobBulkResult>> BulkUploadAsync(
        string container,
        IReadOnlyCollection<BlobUploadRequest> blobs,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(container);
        Argument.IsNotNull(blobs);

        if (blobs.Count == 0)
        {
            return [];
        }

        // Index results by enumeration position so results[i] describes items[i] (parallel bodies start out of order).
        var items = blobs as IReadOnlyList<BlobUploadRequest> ?? [.. blobs];
        var results = new BlobBulkResult[items.Count];

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
                        var location = new BlobLocation(container, blob.Path);
                        await UploadAsync(location, blob.Stream, blob.Metadata, ct).ConfigureAwait(false);
                        results[i] = new BlobBulkResult(location, Result<bool, Exception>.Ok(true));
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        results[i] = new BlobBulkResult(container, blob.Path, Result<bool, Exception>.Fail(e));
                    }
                }
            )
            .ConfigureAwait(false);

        return results;
    }

    #endregion

    #region Delete

    public async ValueTask<bool> DeleteAsync(BlobLocation location, CancellationToken cancellationToken = default)
    {
        var (blobPath, sidecarPath) = _ResolvePaths(location);

        logger.LogDeletingBlob(blobPath);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var deleted = await _DeleteFileIfExistsAsync(client, blobPath, cancellationToken).ConfigureAwait(false);

            // Remove the metadata sidecar alongside the blob so a later re-upload of the same key without metadata
            // cannot resurrect stale metadata (AE8). A missing sidecar is a no-op.
            await _DeleteFileIfExistsAsync(client, sidecarPath, cancellationToken).ConfigureAwait(false);

            return deleted;
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    public async ValueTask<IReadOnlyList<BlobBulkResult>> BulkDeleteAsync(
        string container,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(container);
        Argument.IsNotNull(paths);

        if (paths.Count == 0)
        {
            return [];
        }

        // Index results by input position (see BulkUploadAsync) so each entry matches its key in original order.
        var items = paths as IReadOnlyList<string> ?? [.. paths];
        var results = new BlobBulkResult[items.Count];

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
                    var path = items[i];

                    try
                    {
                        // Build the location inside the try (validates + resolves through the single seam) so an
                        // unaddressable key fails that one item without aborting the batch.
                        var location = new BlobLocation(container, path);
                        var deleted = await DeleteAsync(location, ct).ConfigureAwait(false);
                        results[i] = new BlobBulkResult(location, Result<bool, Exception>.Ok(deleted));
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        results[i] = new BlobBulkResult(container, path, Result<bool, Exception>.Fail(e));
                    }
                }
            )
            .ConfigureAwait(false);

        return results;
    }

    public async ValueTask<int> DeleteAllAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(query);

        // Resolve container + prefix through the single seam (the prefix was already path-security validated at
        // BlobQuery construction), so delete-by-prefix cannot escape into traversal or an un-normalized container.
        var (container, prefix) = BlobLocationResolver.ResolveQuery(query, normalizer);

        logger.LogDeletingAllByPrefix(prefix);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Collect matches first, then delete: deleting while enumerating would mutate the directory under the
            // recursive listing. Each matched blob's sidecar is deleted alongside it.
            var toDelete = new List<string>();
            var count = 0;

            await foreach (
                var (key, _) in _EnumerateBlobsAsync(client, container, relativePrefix: "", cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                // Sidecars are deleted as companions of their blob, never matched directly.
                if (BlobStorageHelpers.IsSidecarKey(key))
                {
                    continue;
                }

                if (prefix is not null && !key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var blobPath = _CombinePath(container, key);
                toDelete.Add(blobPath);
                toDelete.Add(blobPath + BlobStorageHelpers.SidecarSuffix);
                count++;
            }

            foreach (var path in toDelete)
            {
                await _DeleteFileIfExistsAsync(client, path, cancellationToken).ConfigureAwait(false);
            }

            return count;
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    #endregion

    #region Move / Copy

    public async ValueTask<bool> CopyAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    )
    {
        var (sourcePath, sourceSidecar) = _ResolvePaths(source);
        var (destPath, destSidecar) = _ResolvePaths(destination);

        logger.LogCopyingBlob(sourcePath, destPath);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using (
                var sourceStream = await client
                    .OpenAsync(sourcePath, FileMode.Open, FileAccess.Read, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                await _EnsureParentDirectoryAsync(client, destPath, cancellationToken).ConfigureAwait(false);

                await using var destStream = await client
                    .OpenAsync(destPath, FileMode.Create, FileAccess.Write, cancellationToken)
                    .ConfigureAwait(false);

                await sourceStream.CopyToAsync(destStream, cancellationToken).ConfigureAwait(false);
            }

            // Move the sidecar with the blob. If the source has no sidecar, drop any stale destination sidecar so the
            // copied blob does not inherit the previous occupant's metadata.
            var sourceMetadata = await _ReadSidecarAsync(client, sourceSidecar, cancellationToken)
                .ConfigureAwait(false);

            if (sourceMetadata is not null)
            {
                await _WriteSidecarAsync(client, destSidecar, sourceMetadata, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _DeleteFileIfExistsAsync(client, destSidecar, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (SftpPathNotFoundException ex)
        {
            logger.LogCopySourceNotFound(ex, sourcePath);

            return false;
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    public async ValueTask<bool> MoveAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    )
    {
        // Move is a non-atomic copy-then-delete (folds L4). Unlike the old rename, the destination is NOT pre-deleted:
        // the copy overwrites an existing destination in place, and the source is removed only after the copy
        // succeeds, so a failed move never destroys a pre-existing destination ahead of time. If deleting the source
        // fails after a successful copy, a best-effort rollback deletes the destination copy to preserve the original.
        // The metadata sidecar moves with the blob (CopyAsync copies it, DeleteAsync removes the source's).
        if (!await CopyAsync(source, destination, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            await DeleteAsync(source, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogMoveRollback(e, source.ToString(), destination.ToString());

            // Compensating delete so the original is preserved; swallow a rollback failure (best-effort).
            try
            {
                await DeleteAsync(destination, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollbackError)
            {
                logger.LogMoveRollbackFailed(rollbackError, destination.ToString());
            }

            throw;
        }
    }

    #endregion

    #region Exists

    public async ValueTask<bool> ExistsAsync(BlobLocation location, CancellationToken cancellationToken = default)
    {
        var (blobPath, _) = _ResolvePaths(location);

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

    #endregion

    #region Download / Info

    public async ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
        var (blobPath, sidecarPath) = _ResolvePaths(location);

        logger.LogGettingFileStream(blobPath);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Read the sidecar first (fully consumed and its handle closed) so the content handle below is free to own
            // the pooled client until the caller disposes the returned stream.
            var metadata = await _ReadSidecarAsync(client, sidecarPath, cancellationToken).ConfigureAwait(false);

            var sftpStream = await client
                .OpenAsync(blobPath, FileMode.Open, FileAccess.Read, cancellationToken)
                .ConfigureAwait(false);

            var wrappedStream = new PooledClientStream(sftpStream, client, pool);

#pragma warning disable CA2000 // Dispose objects before losing scope - ownership transferred to caller
            return new BlobDownloadResult(wrappedStream, location.Path, BlobStorageHelpers.ToUserMetadata(metadata));
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
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
        // Resolve through the single seam (folds H3 on this path too): the key is validated + normalized identically
        // to where the blob was written, so info and upload address the same file.
        var (container, key) = BlobLocationResolver.Resolve(location, normalizer);
        var blobPath = _CombinePath(container, key);
        var sidecarPath = blobPath + BlobStorageHelpers.SidecarSuffix;

        logger.LogGettingBlobInfo(blobPath);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ISftpFile file;

            try
            {
                file = await client.GetAsync(blobPath, cancellationToken).ConfigureAwait(false);
            }
            catch (SftpPathNotFoundException ex)
            {
                logger.LogBlobInfoNotFound(ex, blobPath);

                return null;
            }

            if (file.IsDirectory)
            {
                logger.LogBlobInfoIsDirectory(blobPath);

                return null;
            }

            var metadata = await _ReadSidecarAsync(client, sidecarPath, cancellationToken).ConfigureAwait(false);

            return _ToBlobInfo(file, key, metadata);
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    #endregion

    #region List

    public async ValueTask<BlobPage> ListAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(query);

        var (container, prefix) = BlobLocationResolver.ResolveQuery(query, normalizer);

        var startAfter = _DecodeToken(query.ContinuationToken);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // SFTP has no server-side listing filter, but a path-like prefix can still start recursion at the deepest
            // safe directory implied by that prefix. File-name-only prefixes fall back to the container root.
            var (startDirectory, relativePrefix) = GetEnumerationScope(container, prefix);
            var matches = new List<BlobInfo>();

            await foreach (
                var (key, file) in _EnumerateBlobsAsync(client, startDirectory, relativePrefix, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                if (BlobStorageHelpers.IsSidecarKey(key))
                {
                    continue;
                }

                if (prefix is not null && !key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                // List omits per-object metadata (it would cost a sidecar read per file); GetBlobInfoAsync is the
                // authoritative source for Metadata and the sidecar-derived Created timestamp.
                matches.Add(_ToBlobInfo(file, key, metadata: null));
            }

            matches.Sort(static (a, b) => string.CompareOrdinal(a.BlobKey, b.BlobKey));

            IEnumerable<BlobInfo> ordered = matches;

            if (startAfter is not null)
            {
                ordered = ordered.Where(b => string.CompareOrdinal(b.BlobKey, startAfter) > 0);
            }

            // Take one extra to detect whether a further page exists without a second scan.
            var page = ordered.Take(query.PageSize + 1).ToList();

            string? continuationToken = null;

            if (page.Count > query.PageSize)
            {
                page.RemoveAt(page.Count - 1);
                continuationToken = _EncodeToken(page[^1].BlobKey);
            }

            return new BlobPage(page, continuationToken);
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    #endregion

    #region Path / Sidecar Helpers

    private (string BlobPath, string SidecarPath) _ResolvePaths(BlobLocation location)
    {
        var (container, key) = BlobLocationResolver.Resolve(location, normalizer);
        var blobPath = _CombinePath(container, key);

        return (blobPath, blobPath + BlobStorageHelpers.SidecarSuffix);
    }

    private static string _CombinePath(string container, string key)
    {
        return string.IsNullOrEmpty(key) ? container : $"{container}/{key}";
    }

    internal static (string Directory, string RelativePrefix) GetEnumerationScope(string container, string? prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return (container, "");
        }

        var lastSlash = prefix.LastIndexOf('/');

        if (lastSlash < 0)
        {
            return (container, "");
        }

        var directoryPrefix = prefix[..lastSlash];

        if (directoryPrefix.Length == 0)
        {
            return (container, "");
        }

        return ($"{container}/{directoryPrefix}", prefix[..(lastSlash + 1)]);
    }

    private Dictionary<string, string> _BuildSidecarPayload(IReadOnlyDictionary<string, string>? metadata, string path)
    {
        // Copy the caller's metadata (never mutate it), then layer the framework keys on top so they are always
        // present. uploadDate is what GetBlobInfoAsync uses to recover the blob's Created timestamp.
        var payload = new Dictionary<string, string>(StringComparer.Ordinal);

        if (metadata is not null)
        {
            foreach (var pair in metadata)
            {
                payload[pair.Key] = pair.Value;
            }
        }

        payload[BlobStorageHelpers.UploadDateMetadataKey] = timeProvider.GetUtcNow().ToString("O");
        payload[BlobStorageHelpers.ExtensionMetadataKey] = Path.GetExtension(path);

        return payload;
    }

    private async Task _WriteSidecarAsync(
        SftpClient client,
        string sidecarPath,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken
    )
    {
        var bytes = serializer.SerializeToBytes(payload)!;

        await using var sidecarStream = await client
            .OpenAsync(sidecarPath, FileMode.Create, FileAccess.Write, cancellationToken)
            .ConfigureAwait(false);

        await sidecarStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, string>?> _ReadSidecarAsync(
        SftpClient client,
        string sidecarPath,
        CancellationToken cancellationToken
    )
    {
        Stream sidecarStream;

        try
        {
            sidecarStream = await client
                .OpenAsync(sidecarPath, FileMode.Open, FileAccess.Read, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SftpPathNotFoundException)
        {
            // A missing sidecar reads as no metadata (blob written out-of-band, or before metadata support).
            return null;
        }

        await using (sidecarStream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await sidecarStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (buffer.Length == 0)
            {
                return null;
            }

            return serializer.Deserialize<Dictionary<string, string>>(buffer.ToArray());
        }
    }

    private static async Task _WriteAllAsync(
        SftpClient client,
        string blobPath,
        Stream content,
        CancellationToken cancellationToken
    )
    {
        await using var sftpFileStream = await client
            .OpenAsync(blobPath, FileMode.Create, FileAccess.Write, cancellationToken)
            .ConfigureAwait(false);

        await content.CopyToAsync(sftpFileStream, cancellationToken).ConfigureAwait(false);
    }

    private async Task _EnsureParentDirectoryAsync(SftpClient client, string path, CancellationToken cancellationToken)
    {
        var lastSlash = path.LastIndexOf('/');

        if (lastSlash <= 0)
        {
            // Top-level file (no parent directory beyond the connection root).
            return;
        }

        var directory = path[..lastSlash];

        // The segments come from the already-resolved (validated + normalized) blob path, so directory creation can
        // never act on a raw, un-validated segment — this is the upload/move half of the H3 fold.
        var segments = directory.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return;
        }

        var current = string.Empty;

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            current = current.Length == 0 ? segment : $"{current}/{segment}";

            if (await client.ExistsAsync(current, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (i == 0)
            {
                throw new SftpPathNotFoundException(
                    $"Blob container '{segment}' does not exist. Ensure it through IBlobContainerManager before uploading."
                );
            }

            logger.LogCreatingContainerSegment(segment);

            try
            {
                await client.CreateDirectoryAsync(current, cancellationToken).ConfigureAwait(false);
            }
            catch (SshException)
            {
                // A concurrent upload may have created this directory between the existence check and the create;
                // tolerate that race, but rethrow if the directory still does not exist.
                if (!await client.ExistsAsync(current, cancellationToken).ConfigureAwait(false))
                {
                    throw;
                }
            }
        }
    }

    private static async Task<bool> _DeleteFileIfExistsAsync(
        SftpClient client,
        string path,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await client.DeleteFileAsync(path, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (SftpPathNotFoundException)
        {
            return false;
        }
    }

    #endregion

    #region Enumeration

    /// <summary>
    /// Recursively yields every regular file under <paramref name="sftpDirectory"/> as
    /// <c>(container-relative key, file)</c>. The relative key is tracked through the recursion rather than derived
    /// from absolute paths, so it is independent of the connection's working directory.
    /// </summary>
    private async IAsyncEnumerable<(string ObjectKey, ISftpFile File)> _EnumerateBlobsAsync(
        SftpClient client,
        string sftpDirectory,
        string relativePrefix,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (cancellationToken.IsCancellationRequested)
        {
            logger.LogCancellationRequested();
            yield break;
        }

        IAsyncEnumerable<ISftpFile> files;

        try
        {
            files = client.ListDirectoryAsync(sftpDirectory, cancellationToken);
        }
        catch (SftpPathNotFoundException)
        {
            logger.LogDirectoryNotFound(sftpDirectory);
            yield break;
        }

        await foreach (var file in _SafeListDirectoryAsync(files, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogCancellationRequested();
                yield break;
            }

            if (file.Name is "." or "..")
            {
                continue;
            }

            var relativeKey = relativePrefix.Length == 0 ? file.Name : $"{relativePrefix}{file.Name}";

            if (file.IsDirectory)
            {
                await foreach (
                    var item in _EnumerateBlobsAsync(
                            client,
                            $"{sftpDirectory}/{file.Name}",
                            $"{relativeKey}/",
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                )
                {
                    yield return item;
                }

                continue;
            }

            if (!file.IsRegularFile)
            {
                continue;
            }

            yield return (relativeKey, file);
        }
    }

    private static async IAsyncEnumerable<ISftpFile> _SafeListDirectoryAsync(
        IAsyncEnumerable<ISftpFile> files,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var enumerator = files.GetAsyncEnumerator(cancellationToken);
        await using (enumerator.ConfigureAwait(false))
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
                    // Directory was deleted during iteration - stop enumeration.
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

    #endregion

    #region Mapping / Tokens

    private static BlobInfo _ToBlobInfo(ISftpFile file, string objectKey, IReadOnlyDictionary<string, string>? metadata)
    {
        var modified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);

        return new BlobInfo
        {
            BlobKey = objectKey,
            // Prefer the sidecar's recorded upload date when present; otherwise SFTP only exposes the last-write time.
            // Created reads the raw sidecar (framework keys intact); the returned Metadata exposes caller keys only.
            Created = _GetCreated(metadata, modified),
            Modified = modified,
            Size = file.Length,
            Metadata = BlobStorageHelpers.ToUserMetadata(metadata),
        };
    }

    private static DateTimeOffset _GetCreated(IReadOnlyDictionary<string, string>? metadata, DateTimeOffset fallback)
    {
        if (
            metadata is not null
            && metadata.TryGetValue(BlobStorageHelpers.UploadDateMetadataKey, out var value)
            && DateTimeOffset.TryParseExact(
                value,
                "o",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed
            )
        )
        {
            return parsed;
        }

        return fallback;
    }

    // The continuation token is the last key of the previous page (the start-after-key), Base64-encoded so callers
    // treat it as opaque and round-trip it without parsing.
    private static string _EncodeToken(string key)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(key));
    }

    private static string? _DecodeToken(string? token)
    {
        return string.IsNullOrEmpty(token) ? null : Encoding.UTF8.GetString(Convert.FromBase64String(token));
    }

    #endregion

    public ValueTask DisposeAsync()
    {
        // The pool is a DI-owned singleton shared with the container manager - it is not disposed here.
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
