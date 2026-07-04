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
    // Writes create only container-relative parent directories; a missing top-level container directory throws.
    public bool RequiresContainerProvisioning => true;

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

        // Copies the caller's metadata (never mutated) and layers the framework keys on top; uploadDate is what
        // GetBlobInfoAsync uses to recover the blob's Created timestamp.
        var payload = BlobStorageHelpers.BuildEffectiveMetadata(metadata, timeProvider.GetUtcNow(), location.Path);

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

        return await BlobStorageHelpers
            .RunBulkAsync(
                container,
                blobs,
                options.CurrentValue.MaxConcurrentOperations,
                static blob => blob.Path,
                async (location, blob, ct) =>
                {
                    await UploadAsync(location, blob.Stream, blob.Metadata, ct).ConfigureAwait(false);
                    return true;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
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

        return await BlobStorageHelpers
            .RunBulkAsync(
                container,
                paths,
                options.CurrentValue.MaxConcurrentOperations,
                static path => path,
                async (location, _, ct) => await DeleteAsync(location, ct).ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
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

            // Attempt every matched entry even after a failure: per-entry errors are collected and surfaced as one
            // AggregateException at the end (the uniform DeleteAll contract), so one bad entry cannot abort the rest.
            List<Exception>? failures = null;

            foreach (var path in toDelete)
            {
                try
                {
                    await _DeleteFileIfExistsAsync(client, path, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    (failures ??= []).Add(e);
                }
            }

            if (failures is { Count: > 0 })
            {
                throw new AggregateException($"DeleteAllAsync failed for {failures.Count} blob(s).", failures);
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

        if (string.Equals(sourcePath, destPath, StringComparison.Ordinal))
        {
            // A resolved self-copy is a no-op: opening destPath with FileMode.Create would truncate the source.
            return true;
        }

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
        // Move is a non-atomic copy-then-delete (folds L4) that rejects an occupied destination: an existing
        // destination is never overwritten (Move returns false), and the source is removed only after the copy
        // succeeds. The metadata sidecar moves with the blob (CopyAsync copies it, DeleteAsync removes the
        // source's). The source delete is two-step (blob file, then sidecar), so a sidecar-second fault can leave
        // the source blob already gone — the shared helper rolls the destination copy back only when the source
        // blob is confirmed intact (see MoveViaCopyThenDeleteAsync).
        var (sourceBlobPath, sourceSidecarPath) = _ResolvePaths(source);

        if (string.Equals(sourceBlobPath, _ResolvePaths(destination).BlobPath, StringComparison.Ordinal))
        {
            // A resolved self-move is a no-op: copy-then-delete on the same path would zero then delete the blob.
            return true;
        }

        return await BlobStorageHelpers
            .MoveViaCopyThenDeleteAsync(
                destinationExistsAsync: ct => ExistsAsync(destination, ct),
                copyAsync: ct => CopyAsync(source, destination, ct),
                deleteSourceAsync: ct => DeleteAsync(source, ct),
                sourceExistsAsync: ct => ExistsAsync(source, ct),
                rollbackDestinationAsync: async deleteException =>
                {
                    logger.LogMoveRollback(deleteException, source.ToString(), destination.ToString());

                    // Compensating delete so the original is preserved; swallow a rollback failure (best-effort).
                    try
                    {
                        await DeleteAsync(destination, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception rollbackError)
                    {
                        logger.LogMoveRollbackFailed(rollbackError, destination.ToString());
                    }
                },
                logDestinationKeptSourceGone: e =>
                    logger.LogMoveKeptDestinationSourceGone(e, sourceBlobPath, sourceSidecarPath),
                logSourceCheckFailed: e => logger.LogMoveSourceCheckFailed(e, sourceBlobPath),
                cancellationToken
            )
            .ConfigureAwait(false);
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

        var startAfter = BlobStorageHelpers.DecodeContinuationToken(query.ContinuationToken);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // SFTP has no server-side listing filter, but a path-like prefix can still start recursion at the deepest
            // safe directory implied by that prefix. File-name-only prefixes fall back to the container root.
            var (startDirectory, relativePrefix) = GetEnumerationScope(container, prefix);

            // Hold at most PageSize+1 items so a full window signals that a further page remains. Compute the +1 in
            // long space and clamp the initial capacity so a caller passing PageSize == int.MaxValue ("everything in
            // one page") cannot overflow the capacity/threshold into a negative value.
            var windowLimit = (long)query.PageSize + 1;
            var pageWindow = new List<BlobInfo>((int)Math.Min(windowLimit, 1024));

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

                if (startAfter is not null && string.CompareOrdinal(key, startAfter) <= 0)
                {
                    continue;
                }

                // List omits per-object metadata by default (it would cost a sidecar read per file); it is populated
                // post-trim only when the caller opts in via BlobQuery.IncludeMetadata. GetBlobInfoAsync remains the
                // authoritative source for Metadata and the sidecar-derived Created timestamp.
                var item = _ToBlobInfo(file, key, metadata: null);

                if (pageWindow.Count < windowLimit)
                {
                    pageWindow.Add(item);
                    continue;
                }

                var maxIndex = BlobStorageHelpers.IndexOfMaxKey(pageWindow, static item => item.BlobKey);

                if (string.CompareOrdinal(item.BlobKey, pageWindow[maxIndex].BlobKey) < 0)
                {
                    pageWindow[maxIndex] = item;
                }
            }

            pageWindow.Sort(static (a, b) => string.CompareOrdinal(a.BlobKey, b.BlobKey));

            string? continuationToken = null;

            if (pageWindow.Count > query.PageSize)
            {
                pageWindow.RemoveAt(pageWindow.Count - 1);
                continuationToken = BlobStorageHelpers.EncodeContinuationToken(pageWindow[^1].BlobKey);
            }

            // Populate metadata only for the final page entries when the caller opts in: one sidecar read per
            // returned blob (not per enumerated file), reusing the acquired client. This also recovers the accurate
            // sidecar-derived Created timestamp that the default null-metadata path falls back to last-write-time for.
            if (query.IncludeMetadata)
            {
                for (var i = 0; i < pageWindow.Count; i++)
                {
                    var current = pageWindow[i];
                    var sidecarPath = _CombinePath(container, current.BlobKey) + BlobStorageHelpers.SidecarSuffix;
                    var rawMetadata = await _ReadSidecarAsync(client, sidecarPath, cancellationToken)
                        .ConfigureAwait(false);

                    pageWindow[i] = new BlobInfo
                    {
                        BlobKey = current.BlobKey,
                        Created = BlobStorageHelpers.ParseUploadDate(rawMetadata, fallback: current.Modified),
                        Modified = current.Modified,
                        Size = current.Size,
                        Metadata = BlobStorageHelpers.ToUserMetadata(rawMetadata),
                    };
                }
            }

            return new BlobPage(pageWindow, continuationToken);
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
            Created = BlobStorageHelpers.ParseUploadDate(metadata, fallback: modified),
            Modified = modified,
            Size = file.Length,
            Metadata = BlobStorageHelpers.ToUserMetadata(metadata),
        };
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
            try
            {
                innerStream.Dispose();
            }
            finally
            {
                pool.Release(client);
                _disposed = true;
            }
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            try
            {
                await innerStream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await pool.ReleaseAsync(client).ConfigureAwait(false);
                _disposed = true;
            }
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }
}
