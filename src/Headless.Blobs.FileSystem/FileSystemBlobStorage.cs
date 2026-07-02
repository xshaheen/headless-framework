// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.Internals;
using Headless.Checks;
using Headless.Primitives;
using Headless.Serializer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using File = System.IO.File;

namespace Headless.Blobs.FileSystem;

/// <summary>
/// <see cref="IBlobStorage"/> implementation backed by the local file system.
/// </summary>
/// <remarks>
/// <para>
/// All blobs are stored under the directory configured via <see cref="FileSystemBlobStorageOptions.BaseDirectoryPath"/>.
/// Every operation — including <see cref="GetBlobInfoAsync"/> — turns its <see cref="BlobLocation"/> into a backend
/// key through the single <see cref="BlobLocationResolver"/> seam, then maps the resolved key to a path under the base
/// directory and re-verifies the resolved full path stays inside it. A blob name that would escape the base directory
/// (traversal, absolute path) throws <see cref="ArgumentException"/> before any disk access (H2).
/// </para>
/// <para>
/// Blob metadata is stored in a companion ("sidecar") file next to the content, named
/// <c>"&lt;blob&gt;" + <see cref="BlobStorageHelpers.SidecarSuffix"/></c>. The write order is content-first then
/// sidecar, so a crash between the two reads back as a blob with no metadata rather than corrupt state. Sidecars are
/// excluded from every listing and are deleted/moved with their blob, so re-uploading a key without metadata cannot
/// resurrect a stale sidecar. Container/bucket lifecycle is not part of this data-plane type — it lives on the
/// separately-registered <see cref="FileSystemBlobContainerManager"/> capability; <see cref="UploadAsync"/> still
/// creates the intermediate path directories inherent to writing a blob.
/// </para>
/// </remarks>
public sealed class FileSystemBlobStorage : IBlobStorage
{
    private readonly string _basePath;
    private readonly IJsonSerializer _serializer;
    private readonly IBlobNamingNormalizer _normalizer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    public FileSystemBlobStorage(
        IOptions<FileSystemBlobStorageOptions> optionsAccessor,
        IJsonSerializer serializer,
        IBlobNamingNormalizer normalizer,
        TimeProvider? timeProvider = null,
        ILogger<FileSystemBlobStorage>? logger = null
    )
    {
        Argument.IsNotNull(optionsAccessor);
        _basePath = optionsAccessor.Value.BaseDirectoryPath.NormalizePath().EnsureEndsWith(Path.DirectorySeparatorChar);
        _serializer = Argument.IsNotNull(serializer);
        _normalizer = Argument.IsNotNull(normalizer);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<FileSystemBlobStorage>.Instance;
    }

    // UploadAsync refuses a missing container directory (only intermediate path directories are created on write).
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

        var (container, key, fullPath) = _ResolveLocation(location);
        var containerDirectory = _ContainerDirectory(container);

        if (!Directory.Exists(containerDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Blob container '{container}' does not exist. Ensure it through IBlobContainerManager before uploading."
            );
        }

        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            // Intermediate path creation is inherent to writing a blob; the top-level container was verified above.
            Directory.CreateDirectory(directory);
        }

        var streamOptions = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
        };

        // Content-first write order (KTD6): close the content file before writing the sidecar so a crash between
        // the two leaves a blob with no sidecar (reads back as no metadata) rather than an orphan sidecar.
        var fileStream = new FileStream(fullPath, streamOptions);

        try
        {
            if (content.CanSeek)
            {
                content.Position = 0;
            }

            await content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await fileStream.DisposeAsync().ConfigureAwait(false);
        }

        await _WriteSidecarAsync(fullPath, key, metadata, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<BlobBulkResult>> BulkUploadAsync(
        string container,
        IReadOnlyCollection<BlobUploadRequest> blobs,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(container);
        Argument.IsNotNull(blobs);

        // maxParallelism: 1 keeps the file-system batch effectively sequential (its ops are synchronous) while sharing
        // the per-item BlobLocation-construct + indexed-result + OCE-filter orchestration with the other providers.
        return await BlobStorageHelpers
            .RunBulkAsync(
                container,
                blobs,
                maxParallelism: 1,
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

    public ValueTask<bool> DeleteAsync(BlobLocation location, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (_, _, fullPath) = _ResolveLocation(location);

        return ValueTask.FromResult(_DeleteBlobAndSidecar(fullPath));
    }

    public async ValueTask<IReadOnlyList<BlobBulkResult>> BulkDeleteAsync(
        string container,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(container);
        Argument.IsNotNull(paths);

        // maxParallelism: 1 keeps the synchronous file-system deletes sequential while sharing the per-item
        // orchestration (BlobLocation construct + indexed result + OCE filter) with the other providers.
        return await BlobStorageHelpers
            .RunBulkAsync(
                container,
                paths,
                maxParallelism: 1,
                static path => path,
                (location, _, _) =>
                {
                    var (_, _, fullPath) = _ResolveLocation(location);

                    return ValueTask.FromResult(_DeleteBlobAndSidecar(fullPath));
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public ValueTask<int> DeleteAllAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        // Resolve the container + prefix through the single seam (both were path-security validated at BlobQuery
        // construction), so delete-by-prefix can never escape into traversal.
        var (container, prefix) = BlobLocationResolver.ResolveQuery(query, _normalizer);
        var containerDirectory = _ContainerDirectory(container);

        if (!Directory.Exists(containerDirectory))
        {
            return ValueTask.FromResult(0);
        }

        var count = 0;
        List<Exception>? failures = null;

        foreach (var file in Directory.EnumerateFiles(containerDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = _ToBlobKey(file, containerDirectory);

            // Sidecars are never deleted on their own — they go with their blob below — and a prefix narrows the set.
            if (BlobStorageHelpers.IsSidecarKey(key))
            {
                continue;
            }

            if (prefix is not null && !key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            // Attempt every matched entry even after a failure: per-entry errors are collected and surfaced as one
            // AggregateException at the end (the uniform DeleteAll contract), so one locked file cannot abort the rest.
            try
            {
                File.Delete(file);

                var sidecar = file + BlobStorageHelpers.SidecarSuffix;

                if (File.Exists(sidecar))
                {
                    File.Delete(sidecar);
                }

                count++;
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

        _logger.LogDeletingByPrefix(count, prefix);

        return ValueTask.FromResult(count);
    }

    private static bool _DeleteBlobAndSidecar(string fullPath)
    {
        var existed = File.Exists(fullPath);

        if (existed)
        {
            File.Delete(fullPath);
        }

        // Always drop the sidecar with the blob so a later re-upload without metadata cannot resurrect stale
        // metadata (this also reaps an orphan sidecar left by a crash mid-write).
        var sidecar = fullPath + BlobStorageHelpers.SidecarSuffix;

        if (File.Exists(sidecar))
        {
            File.Delete(sidecar);
        }

        return existed;
    }

    #endregion

    #region Move / Copy

    public ValueTask<bool> CopyAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (_, _, sourcePath) = _ResolveLocation(source);
        var (destinationContainer, _, destinationPath) = _ResolveLocation(destination);

        if (string.Equals(sourcePath, destinationPath, StringComparison.Ordinal))
        {
            // A resolved self-copy is a no-op: opening destinationPath with FileMode.Create would truncate the source.
            return ValueTask.FromResult(true);
        }

        _logger.LogCopyingFile(sourcePath, destinationPath);

        if (!File.Exists(sourcePath))
        {
            return ValueTask.FromResult(false);
        }

        // Same data-plane rule as UploadAsync: never auto-create the top-level container. Without this guard the
        // intermediate-directory creation below would silently provision the destination container.
        if (!Directory.Exists(_ContainerDirectory(destinationContainer)))
        {
            throw new DirectoryNotFoundException(
                $"Blob container '{destinationContainer}' does not exist. Ensure it through IBlobContainerManager before copying."
            );
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);

        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);

        // The sidecar moves with the blob: copy it when present, otherwise drop a stale destination sidecar so an
        // overwrite without source metadata cannot resurrect the destination's old metadata.
        var sourceSidecar = sourcePath + BlobStorageHelpers.SidecarSuffix;
        var destinationSidecar = destinationPath + BlobStorageHelpers.SidecarSuffix;

        if (File.Exists(sourceSidecar))
        {
            File.Copy(sourceSidecar, destinationSidecar, overwrite: true);
        }
        else if (File.Exists(destinationSidecar))
        {
            File.Delete(destinationSidecar);
        }

        return ValueTask.FromResult(true);
    }

    public async ValueTask<bool> MoveAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    )
    {
        var (_, _, sourceFullPath) = _ResolveLocation(source);
        var (_, _, destinationFullPath) = _ResolveLocation(destination);

        if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.Ordinal))
        {
            // A resolved self-move is a no-op: copy-then-delete on the same path would delete the blob.
            return true;
        }

        // Non-atomic copy-then-delete; the sidecar moves with the blob (KTD6/KTD7). The shared helper owns
        // reject-occupied and the rollback rule: the source delete is two-step (blob file, then sidecar), so a
        // sidecar-second fault can leave the source blob already gone — the destination copy is rolled back only
        // when the source blob is confirmed intact (see MoveViaCopyThenDeleteAsync).
        return await BlobStorageHelpers
            .MoveViaCopyThenDeleteAsync(
                destinationExistsAsync: _ => ValueTask.FromResult(File.Exists(destinationFullPath)),
                copyAsync: ct => CopyAsync(source, destination, ct),
                deleteSourceAsync: _ =>
                {
                    _logger.LogMovingFile(sourceFullPath);

                    return ValueTask.FromResult(_DeleteBlobAndSidecar(sourceFullPath));
                },
                sourceExistsAsync: _ => ValueTask.FromResult(File.Exists(sourceFullPath)),
                rollbackDestinationAsync: deleteException =>
                {
                    _logger.LogFailedToDeleteOriginal(deleteException, sourceFullPath);

                    try
                    {
                        _DeleteBlobAndSidecar(destinationFullPath);
                    }
                    catch (Exception rollbackException)
                    {
                        _logger.LogFailedToRollbackDestination(rollbackException, destinationFullPath);
                    }

                    return ValueTask.CompletedTask;
                },
                logDestinationKeptSourceGone: e =>
                    _logger.LogMoveKeptDestinationSourceGone(
                        e,
                        sourceFullPath,
                        sourceFullPath + BlobStorageHelpers.SidecarSuffix
                    ),
                logSourceCheckFailed: e => _logger.LogMoveSourceCheckFailed(e, sourceFullPath),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Exists

    public ValueTask<bool> ExistsAsync(BlobLocation location, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (_, _, fullPath) = _ResolveLocation(location);

        return ValueTask.FromResult(File.Exists(fullPath));
    }

    #endregion

    #region Download / Info

    public async ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (_, _, fullPath) = _ResolveLocation(location);

        _logger.LogGettingFileStream(fullPath);

        if (!File.Exists(fullPath))
        {
            _logger.LogFileNotFound(fullPath);

            return null;
        }

        // Read the sidecar before opening the content stream so no stream is held open across an awaited read that
        // could throw — the stream is opened last and its ownership transfers to the result in the same step.
        var metadata = await _ReadSidecarMetadataAsync(fullPath, cancellationToken).ConfigureAwait(false);

        try
        {
            var fileStream = File.OpenRead(fullPath);

#pragma warning disable CA2000 // Ownership transfers to the returned BlobDownloadResult ([MustDisposeResource]).
            // FileName is the full container-relative key (location.Path), matching AWS/Azure/Redis/SSH — not just the
            // last path segment.
            return new BlobDownloadResult(fileStream, location.Path, BlobStorageHelpers.ToUserMetadata(metadata));
#pragma warning restore CA2000
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // The file was removed between the existence check and the open (TOCTOU); honor the null contract.
            _logger.LogFileNotFound(fullPath);

            return null;
        }
    }

    public async ValueTask<BlobInfo?> GetBlobInfoAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
        // H2 fold: resolve through the single seam (which re-checks the resolved full path for traversal) before
        // touching disk, so a name like "../../etc/passwd" is rejected instead of leaking existence/size/timestamps.
        var (_, key, fullPath) = _ResolveLocation(location);

        _logger.LogGettingFileStream(fullPath);

        var fileInfo = new FileInfo(fullPath);

        if (!fileInfo.Exists)
        {
            _logger.LogFileNotFound(fullPath);

            return null;
        }

        var metadata = await _ReadSidecarMetadataAsync(fullPath, cancellationToken).ConfigureAwait(false);

        return _CreateBlobInfo(fileInfo, key, metadata);
    }

    #endregion

    #region List

    public async ValueTask<BlobPage> ListAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(query);

        var (container, prefix) = BlobLocationResolver.ResolveQuery(query, _normalizer);
        var containerDirectory = _ContainerDirectory(container);

        if (!Directory.Exists(containerDirectory))
        {
            return BlobPage.Empty;
        }

        var startAfterKey = BlobStorageHelpers.DecodeContinuationToken(query.ContinuationToken);

        // Hold at most PageSize+1 entries in a sliding window (mirroring the SFTP provider) so memory stays
        // O(pageSize) regardless of how many blobs the container holds — a full window signals a further page remains.
        // Compute the +1 in long space and clamp the initial capacity so PageSize == int.MaxValue ("everything in one
        // page") cannot overflow the capacity/threshold into a negative value.
        var windowLimit = (long)query.PageSize + 1;
        var pageWindow = new List<(string Key, FileInfo Info)>((int)Math.Min(windowLimit, 1024));

        foreach (var file in Directory.EnumerateFiles(containerDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = _ToBlobKey(file, containerDirectory);

            if (BlobStorageHelpers.IsSidecarKey(key))
            {
                continue;
            }

            if (prefix is not null && !key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (startAfterKey is not null && string.CompareOrdinal(key, startAfterKey) <= 0)
            {
                continue;
            }

            var info = new FileInfo(file);

            if (!info.Exists)
            {
                continue;
            }

            if (pageWindow.Count < windowLimit)
            {
                pageWindow.Add((key, info));
                continue;
            }

            var maxIndex = BlobStorageHelpers.IndexOfMaxKey(pageWindow, static item => item.Key);

            if (string.CompareOrdinal(key, pageWindow[maxIndex].Key) < 0)
            {
                pageWindow[maxIndex] = (key, info);
            }
        }

        pageWindow.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));

        string? continuationToken = null;

        if (pageWindow.Count > query.PageSize)
        {
            pageWindow.RemoveAt(pageWindow.Count - 1);
            continuationToken = BlobStorageHelpers.EncodeContinuationToken(pageWindow[^1].Key);
        }

        // List omits per-object metadata by default (it would cost a sidecar read per file); populate it only when the
        // caller opts in via BlobQuery.IncludeMetadata. Sidecars are read only for the final page entries (not the
        // ones evicted from the window), and GetBlobInfoAsync remains the authoritative single-blob metadata source.
        var items = new List<BlobInfo>(pageWindow.Count);

        foreach (var (key, info) in pageWindow)
        {
            var metadata = query.IncludeMetadata
                ? await _ReadSidecarMetadataAsync(info.FullName, cancellationToken).ConfigureAwait(false)
                : null;

            items.Add(_CreateBlobInfo(info, key, metadata));
        }

        return new BlobPage(items, continuationToken);
    }

    #endregion

    #region Sidecar Metadata

    private async ValueTask _WriteSidecarAsync(
        string blobFullPath,
        string objectKey,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken
    )
    {
        // Copies the caller's metadata (never mutated) and layers the framework keys on top — mirroring how the
        // object-store providers store them.
        var sidecar = BlobStorageHelpers.BuildEffectiveMetadata(metadata, _timeProvider.GetUtcNow(), objectKey);

        var payload = _serializer.SerializeToBytes(sidecar)!;
        var sidecarPath = blobFullPath + BlobStorageHelpers.SidecarSuffix;

        await File.WriteAllBytesAsync(sidecarPath, payload, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyDictionary<string, string>?> _ReadSidecarMetadataAsync(
        string blobFullPath,
        CancellationToken cancellationToken
    )
    {
        var sidecarPath = blobFullPath + BlobStorageHelpers.SidecarSuffix;

        byte[] payload;

        try
        {
            payload = await File.ReadAllBytesAsync(sidecarPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // A missing sidecar reads back as no metadata, not an error (KTD6).
            return null;
        }

        return _serializer.Deserialize<Dictionary<string, string>>(payload);
    }

    #endregion

    #region Helpers

    private static BlobInfo _CreateBlobInfo(
        FileInfo fileInfo,
        string blobKey,
        IReadOnlyDictionary<string, string>? metadata
    )
    {
        return new BlobInfo
        {
            BlobKey = blobKey,
            // Prefer the sidecar upload-date when present (OQ6); fall back to the file creation time otherwise.
            Created = BlobStorageHelpers.ParseUploadDate(
                metadata,
                fallback: new DateTimeOffset(fileInfo.CreationTimeUtc, TimeSpan.Zero)
            ),
            Modified = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
            Size = fileInfo.Length,
            // Surface only the caller's metadata: the framework-internal bookkeeping keys (upload date, extension)
            // drive Created resolution above but are not user metadata, so a no-metadata upload reads back as no
            // metadata rather than resurrecting framework keys.
            Metadata = BlobStorageHelpers.ToUserMetadata(metadata),
        };
    }

    /// <summary>
    /// Derives the container-relative blob key for <paramref name="fileFullName"/> by stripping the container
    /// directory and normalizing separators to '/', so the same physical blob yields the same key through
    /// <see cref="GetBlobInfoAsync"/> and <see cref="ListAsync"/>.
    /// </summary>
    private static string _ToBlobKey(string fileFullName, string containerDirectory)
    {
        return fileFullName.Replace(containerDirectory, string.Empty, StringComparison.Ordinal).Replace('\\', '/');
    }

    #endregion

    #region Path Resolution

    /// <summary>
    /// Resolves <paramref name="location"/> through the single <see cref="BlobLocationResolver"/> seam, maps the
    /// resolved key to a path under the base directory, and re-verifies the final full path stays inside it (H2).
    /// No method builds a path from a raw, unresolved key.
    /// </summary>
    private (string Container, string Key, string FullPath) _ResolveLocation(BlobLocation location)
    {
        var (container, key) = BlobLocationResolver.Resolve(location, _normalizer);

        var relative = key.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(_basePath, container, relative);

        _ThrowIfPathTraversal(fullPath, nameof(location));

        return (container, key, fullPath);
    }

    private string _ContainerDirectory(string normalizedContainer)
    {
        var directory = Path.Combine(_basePath, normalizedContainer);

        _ThrowIfPathTraversal(directory, nameof(normalizedContainer));

        return directory.EnsureEndsWith(Path.DirectorySeparatorChar);
    }

    private void _ThrowIfPathTraversal(string path, string paramName)
    {
        // Resolve '..'/'.' segments lexically, then verify the result stays under the base directory.
        // Path.GetRelativePath honors the platform's path-casing semantics (case-insensitive on Windows,
        // case-sensitive on Linux), so the boundary check matches how the OS actually resolves the path.
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(_basePath, fullPath);

        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            // A rejected traversal attempt is a security-relevant event; surface it to logs and name the offending
            // resolved path so an operator or agent can see exactly what was blocked.
            _logger.LogPathTraversalRejected(paramName, fullPath);

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
