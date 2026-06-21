// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Headless.Abstractions;
using Headless.Blobs.Azure.Internals;
using Headless.Blobs.Internals;
using Headless.Checks;
using Headless.Primitives;
using Headless.Threading;
using Headless.Urls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Blobs.Azure;

/// <summary>
/// <see cref="IBlobStorage"/> implementation backed by Azure Blob Storage.
/// Also implements <see cref="IPresignedUrlBlobStorage"/> using Azure SAS (Shared Access Signature) tokens.
/// </summary>
/// <remarks>
/// Requires a <see cref="BlobServiceClient"/> registered in DI before calling the setup extension. The client
/// must be built with a <c>StorageSharedKeyCredential</c> or a user-delegation key to generate SAS URIs;
/// a bare SAS-token or anonymous connection will throw <see cref="InvalidOperationException"/> on presigned URL
/// calls.
/// </remarks>
public sealed class AzureBlobStorage(
    BlobServiceClient blobServiceClient,
    IMimeTypeProvider mimeTypeProvider,
    IClock clock,
    IOptions<AzureStorageOptions> optionAccessor,
    IBlobNamingNormalizer normalizer,
    ILogger<AzureBlobStorage> logger
) : IBlobStorage, IPresignedUrlBlobStorage
{
    private readonly AzureStorageOptions _option = optionAccessor.Value;

    // Containers this instance has already ensured exist, so CreateIfNotExists runs at most once per container
    // rather than on every upload/copy. A container is recorded only after a successful create, so a failed
    // ensure is naturally retried. The per-container lock serializes concurrent first-time ensures of the same
    // container into a single create while letting distinct containers be ensured in parallel.
    private readonly ConcurrentDictionary<string, byte> _ensuredContainers = new(StringComparer.Ordinal);
    private readonly KeyedAsyncLock _ensureContainerLock = new();

    #region Create Container

    public async ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(container);

        await _EnsureContainerOnceAsync(_GetContainer(container), cancellationToken);
    }

    private async Task _EnsureContainerOnceAsync(string container, CancellationToken cancellationToken)
    {
        if (_ensuredContainers.ContainsKey(container))
        {
            return;
        }

        using (await _ensureContainerLock.LockAsync(container, cancellationToken).ConfigureAwait(false))
        {
            // Re-check under the lock: a concurrent caller may have ensured this container while we waited.
            if (_ensuredContainers.ContainsKey(container))
            {
                return;
            }

            await blobServiceClient
                .GetBlobContainerClient(container)
                .CreateIfNotExistsAsync(_option.ContainerPublicAccessType, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // Record only after a successful create so a failed ensure is retried next time.
            _ensuredContainers.TryAdd(container, 0);
        }
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

        if (_option.AutoCreateContainer)
        {
            await CreateContainerAsync(container, cancellationToken).ConfigureAwait(false);
        }

        var blobClient = _GetBlobClient(container, blobName);

        var httpHeader = new BlobHttpHeaders
        {
            ContentType = mimeTypeProvider.GetMimeType(blobName),
            CacheControl = _option.CacheControl,
        };

        metadata ??= new Dictionary<string, string?>(StringComparer.Ordinal);
        metadata[BlobStorageHelpers.UploadDateMetadataKey] = clock.UtcNow.ToString("O");
        metadata[BlobStorageHelpers.ExtensionMetadataKey] = Path.GetExtension(blobName);

        if (stream.CanSeek && stream.Position != 0)
        {
            logger.LogStreamPositionReset(stream.Position, blobName);
            stream.Seek(0, SeekOrigin.Begin);
        }

        await blobClient
            .UploadAsync(stream, httpHeader, metadata, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
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

        var results = new Result<Exception>[blobs.Count];
        var index = 0;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _option.MaxBulkParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel
            .ForEachAsync(
                blobs,
                options,
                async (blob, ct) =>
                {
                    var i = Interlocked.Increment(ref index) - 1;

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

    #endregion

    #region Delete

    public async ValueTask<bool> DeleteAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var blobClient = _GetBlobClient(container, blobName);

        var response = await blobClient
            .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return response.Value;
    }

    #endregion

    #region Bulk Delete

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

        var batch = blobServiceClient.GetBlobBatchClient();

        var blobUrls = _NormalizeBlobUrls(container, blobNames);

        try
        {
            var results = await batch
                .DeleteBlobsAsync(blobUrls, DeleteSnapshotsOption.IncludeSnapshots, cancellationToken)
                .ConfigureAwait(false);

            return results.ConvertAll(result => Result<bool, Exception>.Ok(!result.IsError));
        }
        catch (AggregateException e)
            when (e.InnerException is RequestFailedException { Status: 404 } inner
                && string.Equals(inner.ErrorCode, "ContainerNotFound", StringComparison.Ordinal)
            )
        {
            return blobNames.Select(_ => Result<bool, Exception>.Fail(inner)).ToList();
        }
    }

    public async ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    )
    {
        var files = await GetPagedListAsync(container, blobSearchPattern, 500, cancellationToken).ConfigureAwait(false);
        var count = 0;

        do
        {
            var names = files.Blobs.Select(file => file.BlobKey).ToArray();
            var results = await BulkDeleteAsync(container, names, cancellationToken).ConfigureAwait(false);
            count += results.Count(x => x.IsSuccess);
            await files.NextPageAsync(cancellationToken).ConfigureAwait(false);
        } while (files.HasMore);

        logger.LogFinishedDeletingFiles(count, container, blobSearchPattern);

        return count;
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
        Argument.IsNotNullOrEmpty(blobName);
        Argument.IsNotNullOrEmpty(blobContainer);
        Argument.IsNotNullOrEmpty(newBlobName);
        Argument.IsNotNullOrEmpty(newBlobContainer);

        if (_option.AutoCreateContainer)
        {
            await CreateContainerAsync(newBlobContainer, cancellationToken).ConfigureAwait(false);
        }

        var oldBlobClient = _GetBlobClient(blobContainer, blobName);
        var newBlobClient = _GetBlobClient(newBlobContainer, newBlobName);

        try
        {
            var copyResult = await newBlobClient
                .StartCopyFromUriAsync(oldBlobClient.Uri, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await copyResult.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);

            return copyResult.HasCompleted;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return false;
        }
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
        Argument.IsNotNullOrEmpty(blobName);
        Argument.IsNotNullOrEmpty(blobContainer);
        Argument.IsNotNullOrEmpty(newBlobName);
        Argument.IsNotNullOrEmpty(newBlobContainer);

        var copyResult = await CopyAsync(blobContainer, blobName, newBlobContainer, newBlobName, cancellationToken)
            .ConfigureAwait(false);

        if (!copyResult)
        {
            logger.LogUnableToCopyBlob(blobName, newBlobName);

            return false;
        }

        var deleteResult = await DeleteAsync(blobContainer, blobName, cancellationToken).ConfigureAwait(false);

        if (!deleteResult)
        {
            // Rollback: delete the copy to avoid data duplication
            await DeleteAsync(newBlobContainer, newBlobName, cancellationToken).ConfigureAwait(false);
            logger.LogRenameFailedRolledBack(blobName);

            return false;
        }

        return true;
    }

    #endregion

    #region Exists

    public async ValueTask<bool> ExistsAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var blobClient = _GetBlobClient(container, blobName);
        var response = await blobClient.ExistsAsync(cancellationToken).ConfigureAwait(false);

        return response.Value;
    }

    #endregion

    #region Download

    public async ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var blobClient = _GetBlobClient(container, blobName);

        MemoryStream? memoryStream = null;

        try
        {
            memoryStream = new MemoryStream();
            await blobClient.DownloadToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            memoryStream.Seek(0, SeekOrigin.Begin);

            return new(memoryStream, blobName);
        }
        catch (RequestFailedException e)
            when (e.ErrorCode == BlobErrorCode.BlobNotFound || e.ErrorCode == BlobErrorCode.ContainerNotFound)
        {
            if (memoryStream is not null)
            {
                await memoryStream.DisposeAsync();
            }

            return null;
        }
        catch
        {
            if (memoryStream is not null)
            {
                await memoryStream.DisposeAsync();
            }
            throw;
        }
    }

    public async ValueTask<BlobInfo?> GetBlobInfoAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        var blobClient = _GetBlobClient(container, blobName);

        Response<BlobProperties>? blobProperties;

        try
        {
            blobProperties = await blobClient
                .GetPropertiesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }

        if (!blobProperties.HasValue)
        {
            return null;
        }

        return new BlobInfo
        {
            BlobKey = Url.Combine([.. container.Skip(1).Append(blobName)]),
            Size = blobProperties.Value.ContentLength,
            Created = blobProperties.Value.CreatedOn,
            Modified = blobProperties.Value.LastModified,
        };
    }

    #endregion

    #region List

    public async IAsyncEnumerable<BlobInfo> GetBlobsAsync(
        string[] container,
        string? blobSearchPattern = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);

        var containerClient = blobServiceClient.GetBlobContainerClient(_GetContainer(container));
        var normalizedDirs = container.Skip(1).Select(_NormalizeSegment);
        var normalizedPattern = _NormalizeSearchPattern(blobSearchPattern);
        var criteria = BlobStorageHelpers.GetRequestCriteria(normalizedDirs, normalizedPattern);

        await foreach (
            var blobItem in containerClient.GetBlobsAsync(
                traits: BlobTraits.Metadata,
                states: BlobStates.None,
                prefix: criteria.Prefix,
                cancellationToken: cancellationToken
            )
        )
        {
            if (criteria.Pattern?.IsMatch(blobItem.Name) == false)
            {
                continue;
            }

            if (blobItem.Properties.ContentLength is not > 0)
            {
                continue;
            }

            yield return new BlobInfo
            {
                BlobKey = blobItem.Name,
                Size = blobItem.Properties.ContentLength.Value,
                Created = blobItem.Properties.CreatedOn ?? DateTimeOffset.MinValue,
                Modified = blobItem.Properties.LastModified ?? DateTimeOffset.MinValue,
            };
        }
    }

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

        var containerClient = blobServiceClient.GetBlobContainerClient(_GetContainer(container));
        var normalizedDirs = container.Skip(1).Select(_NormalizeSegment);
        var normalizedPattern = _NormalizeSearchPattern(blobSearchPattern);
        var criteria = BlobStorageHelpers.GetRequestCriteria(normalizedDirs, normalizedPattern);

        var result = new PagedFileListResult(
            async (_, token) =>
                await _GetFilesAsync(containerClient, criteria, pageSize, previous: null, token).ConfigureAwait(false)
        );

        await result.NextPageAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    private async Task<AzureNextPageResult> _GetFilesAsync(
        BlobContainerClient client,
        SearchCriteria criteria,
        int pageSize,
        AzureNextPageResult? previous = null,
        CancellationToken cancellationToken = default
    )
    {
        var blobs = new List<BlobInfo>();

        // Start with the extra blob from the previous page if present.
        if (previous?.ExtraBlob is not null)
        {
            blobs.Add(previous.ExtraBlob);
        }

        var pageSizeToLoad = pageSize < int.MaxValue ? pageSize + 1 : pageSize;
        var continuationToken = previous?.ContinuationToken;

        // Only fetch from Azure if we need more blobs.
        if (blobs.Count < pageSizeToLoad && (previous is null || !string.IsNullOrEmpty(continuationToken)))
        {
            var pages = client
                .GetBlobsAsync(
                    traits: BlobTraits.Metadata,
                    states: BlobStates.None,
                    prefix: criteria.Prefix,
                    cancellationToken: cancellationToken
                )
                .AsPages(continuationToken, pageSizeToLoad - blobs.Count);

            // AsPages parameter pageSizeHint is not guaranteed to be respected.
            // The service may return fewer results due to partition boundaries.

            try
            {
                await foreach (var page in pages.WithCancellation(cancellationToken))
                {
                    continuationToken = page.ContinuationToken;

                    foreach (var blobItem in page.Values)
                    {
                        if (criteria.Pattern?.IsMatch(blobItem.Name) == false)
                        {
                            logger.LogSkippingPathPatternMismatch(blobItem.Name);
                            continue;
                        }

                        if (blobItem.Properties.ContentLength is not > 0)
                        {
                            continue;
                        }

                        blobs.Add(
                            new BlobInfo
                            {
                                BlobKey = blobItem.Name,
                                Size = blobItem.Properties.ContentLength.Value,
                                Created = blobItem.Properties.CreatedOn ?? DateTimeOffset.MinValue,
                                Modified = blobItem.Properties.LastModified ?? DateTimeOffset.MinValue,
                            }
                        );

                        if (blobs.Count >= pageSizeToLoad)
                        {
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(page.ContinuationToken) || blobs.Count >= pageSizeToLoad)
                    {
                        break;
                    }
                }
            }
            catch (RequestFailedException e)
                when (e.Status == 404 && string.Equals(e.ErrorCode, "ContainerNotFound", StringComparison.Ordinal))
            {
                return new AzureNextPageResult
                {
                    Success = true,
                    HasMore = false,
                    Blobs = [],
                    AzureNextPageFunc = null,
                };
            }
            catch (Exception e)
            {
                logger.LogErrorGettingBlobs(e, pageSizeToLoad);
                throw;
            }
        }

        var hasMore = blobs.Count > pageSize;
        BlobInfo? extraBlob = null;

        if (hasMore)
        {
            extraBlob = blobs[^1];
            blobs.RemoveAt(blobs.Count - 1);
        }

        return new AzureNextPageResult
        {
            Success = true,
            HasMore = hasMore,
            Blobs = blobs,
            ExtraBlob = extraBlob,
            ContinuationToken = continuationToken,
            AzureNextPageFunc = hasMore
                ? (currentResult, token) => _GetFilesAsync(client, criteria, pageSize, currentResult, token)
                : null,
        };
    }

    #endregion


    #region Build URLs


    #region Presigned Urls

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="blobName"/> or <paramref name="container"/> fails validation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expiry"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the <see cref="BlobServiceClient"/> cannot generate a SAS URI (no account key or user-delegation credentials).</exception>
    public ValueTask<Uri> GetPresignedDownloadUrlAsync(
        string[] container,
        string blobName,
        TimeSpan expiry,
        CancellationToken cancellationToken = default
    )
    {
        return _GetPresignedUrlAsync(container, blobName, expiry, BlobSasPermissions.Read, cancellationToken);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="blobName"/> or <paramref name="container"/> fails validation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expiry"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the <see cref="BlobServiceClient"/> cannot generate a SAS URI (no account key or user-delegation credentials).</exception>
    public ValueTask<Uri> GetPresignedUploadUrlAsync(
        string[] container,
        string blobName,
        TimeSpan expiry,
        CancellationToken cancellationToken = default
    )
    {
        return _GetPresignedUrlAsync(
            container,
            blobName,
            expiry,
            BlobSasPermissions.Create | BlobSasPermissions.Write,
            cancellationToken
        );
    }

    /// <summary>Builds a SAS-signed presigned URL for a blob with the given permissions and expiry.</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="blobName"/> or <paramref name="container"/> fails validation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expiry"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the <see cref="BlobServiceClient"/> was not built with signing credentials and cannot generate a SAS URI.
    /// </exception>
    private ValueTask<Uri> _GetPresignedUrlAsync(
        string[] container,
        string blobName,
        TimeSpan expiry,
        BlobSasPermissions permissions,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsNotNullOrEmpty(blobName);
        Argument.IsNotNullOrEmpty(container);
        Argument.IsPositive(expiry);

        var blobClient = _GetBlobClient(container, blobName);

        if (!blobClient.CanGenerateSasUri)
        {
            throw new InvalidOperationException(
                "The configured BlobServiceClient cannot generate a SAS URI. Presigned URLs require a client built "
                    + "with an account key (StorageSharedKeyCredential) or a user delegation key; a bare SAS-token or "
                    + "anonymous connection cannot sign."
            );
        }

        var builder = new BlobSasBuilder
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            ExpiresOn = clock.UtcNow.Add(expiry),
            // The SAS is a bearer credential; restrict it to HTTPS so it can't be replayed over plaintext HTTP.
            Protocol = SasProtocol.Https,
        };

        builder.SetPermissions(permissions);

        return ValueTask.FromResult(blobClient.GenerateSasUri(builder));
    }

    #endregion

    private BlobClient _GetBlobClient(string[] container, string blobName)
    {
        var (blobContainer, blobPath) = _NormalizeBlob(container, blobName);
        var containerClient = blobServiceClient.GetBlobContainerClient(blobContainer);
        var blobClient = containerClient.GetBlobClient(blobPath);

        return blobClient;
    }

    private List<Uri> _NormalizeBlobUrls(string[] container, IReadOnlyCollection<string> blobNames)
    {
        PathValidation.ValidateContainer(container);
        foreach (var blobName in blobNames)
        {
            PathValidation.ValidatePathSegment(blobName);
        }

        var sb = new StringBuilder(blobServiceClient.Uri.AbsoluteUri);
        if (sb[^1] != '/')
        {
            sb.Append('/');
        }

        for (var i = 0; i < container.Length; i++)
        {
            if (i > 0)
            {
                sb.Append('/');
            }

            // Two-tier: the first segment is the Azure container (strict rules); the rest are blob-path segments.
            sb.Append(i == 0 ? _NormalizeContainerName(container[i]) : _NormalizeSegment(container[i]));
        }

        var prefix = sb.ToString();
        var result = new List<Uri>(blobNames.Count);

        foreach (var blobName in blobNames)
        {
            result.Add(new Uri($"{prefix}/{_NormalizeSlashes(normalizer.NormalizeBlobName(blobName))}"));
        }

        return result;
    }

    private (string Container, string Blob) _NormalizeBlob(string[] container, string blobName)
    {
        PathValidation.ValidateContainer(container);
        PathValidation.ValidatePathSegment(blobName);

        var normalizedBlobName = normalizer.NormalizeBlobName(blobName);

        var sb = new StringBuilder();
        for (var i = 1; i < container.Length; i++)
        {
            if (sb.Length > 0)
            {
                sb.Append('/');
            }

            // Sub-path segments use lenient path-segment normalization (two-tier model).
            sb.Append(_NormalizeSegment(container[i]));
        }
        if (sb.Length > 0)
        {
            sb.Append('/');
        }

        sb.Append(_NormalizeSlashes(normalizedBlobName));

        return (_GetContainer(container), sb.ToString());
    }

    private string _GetContainer(string[] container)
    {
        PathValidation.ValidateContainer(container);
        return _NormalizeContainerName(container[0]);
    }

    private string _NormalizeContainerName(string containerName)
    {
        return _NormalizeSlashes(normalizer.NormalizeContainerName(containerName));
    }

    // Lenient path-segment normalization for sub-container path parts and blob names (two-tier model): the first
    // container segment is the Azure container (strict rules); everything after is part of the blob path.
    private string _NormalizeSegment(string segment)
    {
        return _NormalizeSlashes(normalizer.NormalizeBlobName(segment));
    }

    private static string _NormalizeSlashes(string x)
    {
        return BlobStorageHelpers.NormalizePath(x).RemovePostfix('/').RemovePrefix('/');
    }

    /// <summary>
    /// Normalizes the search pattern's directory segments to match how they're stored.
    /// Directory segments use the same lenient path-segment normalization as stored blob paths (two-tier model).
    /// Only normalizes directory segments, not the final filename/pattern segment.
    /// </summary>
    private string? _NormalizeSearchPattern(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return pattern;
        }

        // First normalize slashes
        pattern = BlobStorageHelpers.NormalizePath(pattern);

        // Split by '/' and normalize directory segments only (not the last segment)
        var segments = pattern.Split('/');
        if (segments.Length <= 1)
        {
            // No directory segments, just a filename/pattern - don't normalize
            return pattern;
        }

        // Normalize all segments except the last one (which is the filename/pattern)
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            // Don't normalize segments containing wildcards
            if (!segment.Contains('*', StringComparison.Ordinal))
            {
                segments[i] = normalizer.NormalizeBlobName(segment);
            }
        }

        return string.Join('/', segments);
    }

    #endregion

    #region Dispose

    public ValueTask DisposeAsync()
    {
        _ensureContainerLock.Dispose();

        return ValueTask.CompletedTask;
    }

    #endregion
}

internal static partial class AzureBlobStorageLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "StreamPositionReset",
        Level = LogLevel.Warning,
        Message = "Stream position was {Position}, resetting to 0 for blob {BlobName}"
    )]
    public static partial void LogStreamPositionReset(this ILogger logger, long position, string blobName);

    [LoggerMessage(
        EventId = 2,
        EventName = "FinishedDeletingFiles",
        Level = LogLevel.Trace,
        Message = "Finished deleting {FileCount} files matching {@Container} {SearchPattern}"
    )]
    public static partial void LogFinishedDeletingFiles(
        this ILogger logger,
        int fileCount,
        string[] container,
        string? searchPattern
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "UnableToCopyBlob",
        Level = LogLevel.Warning,
        Message = "Unable to copy {BlobName} to {NewBlobName}"
    )]
    public static partial void LogUnableToCopyBlob(this ILogger logger, string blobName, string newBlobName);

    [LoggerMessage(
        EventId = 4,
        EventName = "RenameFailedRolledBack",
        Level = LogLevel.Warning,
        Message = "Rename failed for {BlobName}, rolled back copy"
    )]
    public static partial void LogRenameFailedRolledBack(this ILogger logger, string blobName);

    [LoggerMessage(
        EventId = 5,
        EventName = "SkippingPathPatternMismatch",
        Level = LogLevel.Trace,
        Message = "Skipping {Path}: Doesn't match pattern"
    )]
    public static partial void LogSkippingPathPatternMismatch(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 6,
        EventName = "ErrorGettingBlobs",
        Level = LogLevel.Error,
        Message = "Error getting blobs from Azure Storage. PageSizeToLoad={PageSizeToLoad}"
    )]
    public static partial void LogErrorGettingBlobs(this ILogger logger, Exception exception, int pageSizeToLoad);
}
