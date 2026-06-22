// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Headless.Abstractions;
using Headless.Blobs.Internals;
using Headless.Checks;
using Headless.Primitives;
using Headless.Threading;
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

    // De-dupes the container-name normalization warning so it is logged at most once per distinct caller input.
    private readonly ConcurrentDictionary<string, byte> _loggedContainerNameChanges = new(StringComparer.Ordinal);

    #region Create Container

    public async ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(container);

        await _EnsureContainerOnceAsync(_GetContainer(container), cancellationToken).ConfigureAwait(false);
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

        // Copy caller metadata before adding framework keys so we never mutate the caller's dictionary
        // (which may be shared across BulkUploadAsync requests).
        var effectiveMetadata = metadata is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(metadata, StringComparer.Ordinal);
        effectiveMetadata[BlobStorageHelpers.UploadDateMetadataKey] = clock.UtcNow.ToString("O");
        effectiveMetadata[BlobStorageHelpers.ExtensionMetadataKey] = Path.GetExtension(blobName);

        if (stream.CanSeek && stream.Position != 0)
        {
            logger.LogStreamPositionReset(stream.Position, blobName);
            stream.Seek(0, SeekOrigin.Begin);
        }

        await blobClient
            .UploadAsync(stream, httpHeader, effectiveMetadata, cancellationToken: cancellationToken)
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

        // Materialize to an indexed list so each result lands in the slot matching its input position.
        // Parallel.ForEachAsync does not run bodies in enumeration order, so deriving the index from execution
        // order (e.g. via Interlocked) would misalign results with their inputs whenever parallelism > 1.
        var items = blobs as IReadOnlyList<BlobUploadRequest> ?? blobs.ToList();
        var results = new Result<Exception>[items.Count];

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _option.MaxBulkParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel
            .ForEachAsync(
                Enumerable.Range(0, items.Count),
                options,
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

    #endregion

    #region Delete

    public async ValueTask<bool> DeleteAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);
        Argument.IsNotNullOrEmpty(blobName);

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

        // The Azure Blob Batch API caps a single batch at 256 sub-requests, so chunk to stay within the limit.
        // Results are appended in chunk + submission order, preserving the per-input-name ordering contract.
        var results = new List<Result<bool, Exception>>(blobNames.Count);

        try
        {
            foreach (var chunk in blobUrls.Chunk(_MaxBlobBatchSize))
            {
                var responses = await batch
                    .DeleteBlobsAsync(chunk, DeleteSnapshotsOption.IncludeSnapshots, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var response in responses)
                {
                    results.Add(_MapDeleteResponse(response));
                }
            }

            return results;
        }
        catch (AggregateException e)
            when (e
                    .InnerExceptions.OfType<RequestFailedException>()
                    .Any(static inner =>
                        inner.Status == 404
                        && string.Equals(inner.ErrorCode, "ContainerNotFound", StringComparison.Ordinal)
                    )
            )
        {
            // The whole container is missing, so every requested blob is simply "not found" -> Ok(false), matching
            // the per-blob not-found semantics of a single delete rather than reporting an operation failure.
            return blobNames.Select(_ => Result<bool, Exception>.Ok(false)).ToList();
        }
    }

    private const int _MaxBlobBatchSize = 256;

    // Maps a single Azure batch sub-response to a per-blob result: success -> Ok(true); a 404 means the blob was
    // already gone -> Ok(false) ("not found"); any other error (403/429/5xx) -> Fail so callers see the real cause
    // rather than a misleading "not found", per the IBlobStorage.BulkDeleteAsync contract.
    private static Result<bool, Exception> _MapDeleteResponse(Response response)
    {
        if (!response.IsError)
        {
            return Result<bool, Exception>.Ok(true);
        }

        if (response.Status == 404)
        {
            return Result<bool, Exception>.Ok(false);
        }

        return Result<bool, Exception>.Fail(new RequestFailedException(response.Status, response.ReasonPhrase));
    }

    public ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    )
    {
        return DeleteAllAsync(container, blobSearchPattern, pageSize: 500, cancellationToken);
    }

    // pageSize is a test seam so the multi-page deletion path can be exercised without seeding 500+ blobs.
    internal async ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        var files = await GetPagedListAsync(container, blobSearchPattern, pageSize, cancellationToken)
            .ConfigureAwait(false);
        var count = 0;

        // Listed BlobKeys are already container-relative paths (e.g. "subdir/file.txt"). Delete against only the
        // Azure container (container[0]); passing the full multi-segment container would re-apply the sub-path
        // prefix and target non-existent blobs (".../subdir/subdir/file.txt"), deleting nothing.
        var azureContainer = new[] { container[0] };

        // Delete the currently-loaded page first, then advance. Advancing before deleting would drop the final
        // page (the last NextPageAsync sets HasMore=false, exiting the loop with that page still undeleted).
        while (true)
        {
            var names = files.Blobs.Select(file => file.BlobKey).ToArray();

            try
            {
                var results = await BulkDeleteAsync(azureContainer, names, cancellationToken).ConfigureAwait(false);
                count += results.Count(x => x.IsSuccess && x.Value);
            }
            catch (Exception e)
            {
                // Surface the partial progress before propagating so a mid-enumeration failure is not silent.
                logger.LogDeleteAllPartialFailure(e, count, container, blobSearchPattern);
                throw;
            }

            if (!files.HasMore)
            {
                break;
            }

            await files.NextPageAsync(cancellationToken).ConfigureAwait(false);
        }

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

            // WaitForCompletionAsync returns only once the copy has completed (otherwise it throws), so reaching
            // here means the copy succeeded.
            return true;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            // A missing source blob OR container yields a graceful failure (false), not an exception: the
            // cross-provider conformance contract requires copy/rename against a missing container to not throw.
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

        bool deleteResult;

        try
        {
            deleteResult = await DeleteAsync(blobContainer, blobName, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The copy already succeeded but deleting the source threw (e.g. cancellation or a transient error);
            // roll back the copy so we never leave both source and destination behind.
            await _RollbackRenameCopyAsync(newBlobContainer, newBlobName, blobName).ConfigureAwait(false);

            throw;
        }

        if (!deleteResult)
        {
            await _RollbackRenameCopyAsync(newBlobContainer, newBlobName, blobName).ConfigureAwait(false);

            return false;
        }

        return true;
    }

    // Best-effort rollback of a rename's copy. Uses CancellationToken.None so cleanup still runs even when the
    // rename was cancelled, and swallows (logs) failures so the caller still observes the original outcome/exception
    // rather than a rollback error.
    private async Task _RollbackRenameCopyAsync(string[] newBlobContainer, string newBlobName, string blobName)
    {
        try
        {
            await DeleteAsync(newBlobContainer, newBlobName, CancellationToken.None).ConfigureAwait(false);
            logger.LogRenameFailedRolledBack(blobName);
        }
        catch (Exception e)
        {
            logger.LogRenameRollbackFailed(e, blobName, newBlobName);
        }
    }

    #endregion

    #region Exists

    public async ValueTask<bool> ExistsAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);
        Argument.IsNotNullOrEmpty(blobName);

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
        Argument.IsNotNullOrEmpty(container);
        Argument.IsNotNullOrEmpty(blobName);

        var blobClient = _GetBlobClient(container, blobName);

        try
        {
            // Stream lazily from the network instead of buffering the whole blob into a MemoryStream (which OOMs on
            // large blobs). The caller owns the returned BlobDownloadResult and disposes the stream
            // (see IBlobStorage.OpenReadStreamAsync). OpenReadAsync issues the initial request eagerly, so a missing
            // blob/container still surfaces as RequestFailedException here and maps to null.
            var stream = await blobClient.OpenReadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            return new(stream, blobName);
        }
        catch (RequestFailedException e)
            when (e.ErrorCode == BlobErrorCode.BlobNotFound || e.ErrorCode == BlobErrorCode.ContainerNotFound)
        {
            return null;
        }
    }

    public async ValueTask<BlobInfo?> GetBlobInfoAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobName);
        Argument.IsNotNullOrEmpty(container);

        // Resolve the stored, normalized blob path so BlobKey matches what GetBlobsAsync/GetPagedListAsync return
        // (the container-relative path, excluding the container name) rather than the raw caller-supplied input.
        var (blobContainer, blobPath) = _NormalizeBlob(container, blobName);
        var blobClient = blobServiceClient.GetBlobContainerClient(blobContainer).GetBlobClient(blobPath);

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
            BlobKey = blobPath,
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

        var pageable = containerClient.GetBlobsAsync(
            traits: BlobTraits.Metadata,
            states: BlobStates.None,
            prefix: criteria.Prefix,
            cancellationToken: cancellationToken
        );

        // Iterate via an explicit enumerator so a missing container yields no items (matching GetPagedListAsync)
        // rather than throwing — a try/catch cannot wrap a `yield return` directly.
        await using var enumerator = pageable.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            BlobItem blobItem;

            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    break;
                }

                blobItem = enumerator.Current;
            }
            catch (RequestFailedException e)
                when (e.Status == 404 && string.Equals(e.ErrorCode, "ContainerNotFound", StringComparison.Ordinal))
            {
                yield break;
            }

            if (criteria.Pattern?.IsMatch(blobItem.Name) == false)
            {
                continue;
            }

            yield return _ToBlobInfo(blobItem);
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
                await _GetFilesAsync(
                        containerClient,
                        criteria,
                        pageSize,
                        carryOverBlob: null,
                        continuationToken: null,
                        token
                    )
                    .ConfigureAwait(false)
        );

        await result.NextPageAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    // Fetches one page (up to pageSize blobs) and, when more remain, returns a continuation delegate that resumes
    // from the captured carry-over blob + continuation token. A surplus "+1" blob is fetched to detect HasMore and is
    // carried into the next page. carryOverBlob is non-null exactly on continuation calls, so null marks the first page.
    private async Task<NextPageResult> _GetFilesAsync(
        BlobContainerClient client,
        SearchCriteria criteria,
        int pageSize,
        BlobInfo? carryOverBlob,
        string? continuationToken,
        CancellationToken cancellationToken
    )
    {
        var blobs = new List<BlobInfo>();

        if (carryOverBlob is not null)
        {
            blobs.Add(carryOverBlob);
        }

        // Fetch one extra blob beyond pageSize to detect whether another page follows (the "+1" probe).
        var pageSizeToLoad = pageSize < int.MaxValue ? pageSize + 1 : pageSize;

        // Fetch from Azure only when more blobs are needed and more are available (first page, or a non-empty token).
        if (blobs.Count < pageSizeToLoad && (carryOverBlob is null || !string.IsNullOrEmpty(continuationToken)))
        {
            try
            {
                continuationToken = await _CollectBlobsAsync(
                        client,
                        criteria,
                        pageSizeToLoad,
                        continuationToken,
                        blobs,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (RequestFailedException e)
                when (e.Status == 404 && string.Equals(e.ErrorCode, "ContainerNotFound", StringComparison.Ordinal))
            {
                return new NextPageResult
                {
                    Success = true,
                    HasMore = false,
                    Blobs = [],
                    NextPageFunc = null,
                };
            }
            catch (Exception e)
            {
                logger.LogErrorGettingBlobs(e, pageSizeToLoad);
                throw;
            }
        }

        // If we collected more than pageSize, the surplus blob is the probe for the next page; carry it over.
        var hasMore = blobs.Count > pageSize;
        BlobInfo? nextCarryOver = null;

        if (hasMore)
        {
            nextCarryOver = blobs[^1];
            blobs.RemoveAt(blobs.Count - 1);
        }

        var nextContinuationToken = continuationToken;

        return new NextPageResult
        {
            Success = true,
            HasMore = hasMore,
            Blobs = blobs,
            NextPageFunc = hasMore
                ? async (_, token) =>
                    await _GetFilesAsync(client, criteria, pageSize, nextCarryOver, nextContinuationToken, token)
                        .ConfigureAwait(false)
                : null,
        };
    }

    // Reads Azure list pages into <paramref name="blobs"/> until pageSizeToLoad entries are gathered or the service is
    // exhausted, and returns the continuation token to resume from (empty/null when no more results remain).
    private async Task<string?> _CollectBlobsAsync(
        BlobContainerClient client,
        SearchCriteria criteria,
        int pageSizeToLoad,
        string? continuationToken,
        List<BlobInfo> blobs,
        CancellationToken cancellationToken
    )
    {
        var pages = client
            .GetBlobsAsync(
                traits: BlobTraits.Metadata,
                states: BlobStates.None,
                prefix: criteria.Prefix,
                cancellationToken: cancellationToken
            )
            .AsPages(continuationToken, pageSizeToLoad - blobs.Count);

        // AsPages pageSizeHint is not guaranteed; the service may return fewer results due to partition boundaries.
        await foreach (var page in pages.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            continuationToken = page.ContinuationToken;

            foreach (var blobItem in page.Values)
            {
                if (criteria.Pattern?.IsMatch(blobItem.Name) == false)
                {
                    logger.LogSkippingPathPatternMismatch(blobItem.Name);
                    continue;
                }

                blobs.Add(_ToBlobInfo(blobItem));

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

        return continuationToken;
    }

    // Projects an Azure BlobItem into the provider-agnostic BlobInfo, including any list-returned metadata. Zero-byte
    // blobs are intentionally NOT filtered out — an empty blob is a real, listable object.
    private static BlobInfo _ToBlobInfo(BlobItem blobItem)
    {
        return new BlobInfo
        {
            BlobKey = blobItem.Name,
            Size = blobItem.Properties.ContentLength ?? 0,
            Created = blobItem.Properties.CreatedOn ?? DateTimeOffset.MinValue,
            Modified = blobItem.Properties.LastModified ?? DateTimeOffset.MinValue,
            Metadata = blobItem.Metadata is { Count: > 0 }
                ? blobItem.Metadata.ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value, StringComparer.Ordinal)
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
    private async ValueTask<Uri> _GetPresignedUrlAsync(
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

        // Account-key (StorageSharedKeyCredential) clients can sign the SAS locally.
        if (blobClient.CanGenerateSasUri)
        {
            return blobClient.GenerateSasUri(builder);
        }

        // AAD / DefaultAzureCredential / Managed Identity clients have no account key, so fall back to a
        // user-delegation SAS. This requires the identity to hold an RBAC role granting
        // "Microsoft.Storage/storageAccounts/blobServices/generateUserDelegationKey" (e.g. "Storage Blob Delegator")
        // plus a data role for the GET/PUT itself. The delegation key — and therefore the SAS — is capped at 7 days.
        UserDelegationKey userDelegationKey;

        try
        {
            var response = await blobServiceClient
                .GetUserDelegationKeyAsync(startsOn: null, expiresOn: builder.ExpiresOn, cancellationToken)
                .ConfigureAwait(false);

            userDelegationKey = response.Value;
        }
        catch (RequestFailedException e)
        {
            throw new InvalidOperationException(
                "Unable to generate a presigned URL. The BlobServiceClient cannot sign with an account key and "
                    + "requesting a user-delegation key failed. Use a client built with an account key "
                    + "(StorageSharedKeyCredential), or a TokenCredential whose identity has the 'Storage Blob "
                    + "Delegator' role (plus a data role), and keep the requested expiry within 7 days.",
                e
            );
        }

        var sas = builder.ToSasQueryParameters(userDelegationKey, blobServiceClient.AccountName);
        var uriBuilder = new BlobUriBuilder(blobClient.Uri) { Sas = sas };

        return uriBuilder.ToUri();
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

        // Two-tier: the first segment is the Azure container (strict rules); the rest are blob-path segments.
        var sb = new StringBuilder(blobServiceClient.Uri.AbsoluteUri);
        if (sb[^1] != '/')
        {
            sb.Append('/');
        }

        sb.Append(_NormalizeContainerName(container[0]));

        var subPath = _BuildSubPath(container);
        if (subPath.Length > 0)
        {
            sb.Append('/').Append(subPath);
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

        var normalizedBlobName = _NormalizeSlashes(normalizer.NormalizeBlobName(blobName));
        var subPath = _BuildSubPath(container);
        var blobPath = subPath.Length > 0 ? $"{subPath}/{normalizedBlobName}" : normalizedBlobName;

        return (_GetContainer(container), blobPath);
    }

    // Normalizes and joins the sub-container path segments (everything after the first/Azure-container segment) with
    // lenient path-segment rules (two-tier model). Returns an empty string when there are no sub-segments.
    private string _BuildSubPath(string[] container)
    {
        return string.Join('/', container.Skip(1).Select(_NormalizeSegment));
    }

    private string _GetContainer(string[] container)
    {
        PathValidation.ValidateContainer(container);
        return _NormalizeContainerName(container[0]);
    }

    private string _NormalizeContainerName(string containerName)
    {
        var normalized = _NormalizeSlashes(normalizer.NormalizeContainerName(containerName));

        // Surface lossy normalization once per distinct input: two different caller names can normalize to the same
        // Azure container and silently share storage. De-duped to avoid a warning on every operation.
        if (
            !string.Equals(normalized, containerName, StringComparison.Ordinal)
            && _loggedContainerNameChanges.TryAdd(containerName, 0)
        )
        {
            logger.LogContainerNameNormalized(containerName, normalized);
        }

        return normalized;
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
        EventId = 7,
        EventName = "RenameRollbackFailed",
        Level = LogLevel.Error,
        Message = "Rename rollback failed for {BlobName}: could not delete the copied blob {NewBlobName}; a duplicate may remain"
    )]
    public static partial void LogRenameRollbackFailed(
        this ILogger logger,
        Exception exception,
        string blobName,
        string newBlobName
    );

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

    [LoggerMessage(
        EventId = 8,
        EventName = "ContainerNameNormalized",
        Level = LogLevel.Warning,
        Message = "Container name {RequestedContainer} was normalized to {NormalizedContainer} to satisfy Azure naming rules; distinct names that normalize to the same value share one container"
    )]
    public static partial void LogContainerNameNormalized(
        this ILogger logger,
        string requestedContainer,
        string normalizedContainer
    );

    [LoggerMessage(
        EventId = 9,
        EventName = "DeleteAllPartialFailure",
        Level = LogLevel.Error,
        Message = "DeleteAllAsync failed after deleting {DeletedCount} blobs from {@Container} matching {SearchPattern}"
    )]
    public static partial void LogDeleteAllPartialFailure(
        this ILogger logger,
        Exception exception,
        int deletedCount,
        string[] container,
        string? searchPattern
    );
}
