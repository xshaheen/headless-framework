// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Headless.Abstractions;
using Headless.Blobs.Internals;
using Headless.Checks;
using Headless.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Blobs.Azure;

/// <summary>
/// <see cref="IBlobStorage"/> implementation backed by Azure Blob Storage.
/// Also implements <see cref="IPresignedUrlBlobStorage"/> using Azure SAS (Shared Access Signature) tokens.
/// </summary>
/// <remarks>
/// <para>
/// Requires a <see cref="BlobServiceClient"/> registered in DI before calling the setup extension. The client
/// must be built with a <c>StorageSharedKeyCredential</c> or a user-delegation key to generate SAS URIs;
/// a bare SAS-token or anonymous connection will throw <see cref="InvalidOperationException"/> on presigned URL
/// calls.
/// </para>
/// <para>
/// Container lifecycle (create/exists/delete) is intentionally <b>not</b> implemented here — it lives on the
/// separately-registered <see cref="IBlobContainerManager"/> capability (<see cref="AzureBlobContainerManager"/>).
/// <see cref="UploadAsync"/> never auto-creates a missing container; a missing container surfaces as an error.
/// </para>
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
    // The Azure Blob Batch API caps a single batch at 256 sub-requests.
    private const int _MaxBlobBatchSize = 256;

    private readonly AzureStorageOptions _option = optionAccessor.Value;

    #region Upload

    public async ValueTask UploadAsync(
        BlobLocation location,
        Stream content,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(content);

        var (container, key) = BlobLocationResolver.Resolve(location, normalizer);
        var blobClient = blobServiceClient.GetBlobContainerClient(container).GetBlobClient(key);

        var httpHeader = new BlobHttpHeaders
        {
            ContentType = mimeTypeProvider.GetMimeType(location.Path),
            CacheControl = _option.CacheControl,
        };

        // Copy the caller's metadata before adding the framework keys so we never mutate the caller's dictionary
        // (which may be shared across a BulkUploadAsync batch), then layer uploadDate/extension on top so they are
        // always present regardless of what the caller supplied.
        var effectiveMetadata = metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(metadata, StringComparer.Ordinal);

        effectiveMetadata[BlobStorageHelpers.UploadDateMetadataKey] = clock.UtcNow.ToString("O");
        effectiveMetadata[BlobStorageHelpers.ExtensionMetadataKey] = Path.GetExtension(location.Path);

        // Seekable streams are rewound to position 0 before upload. Non-seekable streams are passed straight
        // through to the Azure SDK, which streams them as-is — non-seekable handling is provider-specific and is
        // not a uniform promise (folds M1).
        if (content.CanSeek && content.Position != 0)
        {
            content.Seek(0, SeekOrigin.Begin);
        }

        await blobClient
            .UploadAsync(content, httpHeader, effectiveMetadata, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    #endregion

    #region Bulk Upload

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

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _option.MaxBulkParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel
            .ForAsync(
                0,
                items.Count,
                options,
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
        var (container, key) = BlobLocationResolver.Resolve(location, normalizer);
        var blobClient = blobServiceClient.GetBlobContainerClient(container).GetBlobClient(key);

        var response = await blobClient
            .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return response.Value;
    }

    #endregion

    #region Bulk Delete

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

        var items = paths as IReadOnlyList<string> ?? [.. paths];
        var results = new BlobBulkResult[items.Count];

        // First pass: build the location (validates) and resolve the container + key through the single seam, then
        // materialize the blob URI for the batch API. An unaddressable key (traversal, reserved sidecar suffix,
        // etc.) fails that one item here without aborting the batch; addressable items carry their input index and
        // identity into the chunked batch-delete second pass.
        var batchEntries = new List<(int Index, BlobLocation Location, Uri Uri)>(items.Count);

        for (var i = 0; i < items.Count; i++)
        {
            try
            {
                var location = new BlobLocation(container, items[i]);
                var (azureContainer, key) = BlobLocationResolver.Resolve(location, normalizer);
                var uri = blobServiceClient.GetBlobContainerClient(azureContainer).GetBlobClient(key).Uri;
                batchEntries.Add((i, location, uri));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                results[i] = new BlobBulkResult(container, items[i], Result<bool, Exception>.Fail(e));
            }
        }

        if (batchEntries.Count > 0)
        {
            var batch = blobServiceClient.GetBlobBatchClient();

            // Chunk to stay within the 256 sub-request batch limit. Each chunk's responses are positional, so
            // responses[j] is the outcome for chunk[j].
            foreach (var chunk in batchEntries.Chunk(_MaxBlobBatchSize))
            {
                var uris = Array.ConvertAll(chunk, entry => entry.Uri);

                try
                {
                    var responses = await batch
                        .DeleteBlobsAsync(uris, DeleteSnapshotsOption.IncludeSnapshots, cancellationToken)
                        .ConfigureAwait(false);

                    for (var j = 0; j < chunk.Length; j++)
                    {
                        results[chunk[j].Index] = new BlobBulkResult(
                            chunk[j].Location,
                            _MapDeleteResponse(responses[j])
                        );
                    }
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
                    // The whole container is missing, so every blob in the chunk is simply "not found" -> Ok(false),
                    // matching the per-blob not-found semantics of a single delete rather than an operation failure.
                    foreach (var entry in chunk)
                    {
                        results[entry.Index] = new BlobBulkResult(entry.Location, Result<bool, Exception>.Ok(false));
                    }
                }
                catch (AggregateException e)
                    when (!e.InnerExceptions.Any(static inner => inner is OperationCanceledException))
                {
                    IReadOnlyList<Exception> errors =
                        e.InnerExceptions.Count == chunk.Length
                            ? e.InnerExceptions
                            : Enumerable.Repeat<Exception>(e, chunk.Length).ToArray();

                    for (var j = 0; j < chunk.Length; j++)
                    {
                        results[chunk[j].Index] = new BlobBulkResult(
                            chunk[j].Location,
                            Result<bool, Exception>.Fail(errors[j])
                        );
                    }
                }
                catch (RequestFailedException e)
                {
                    foreach (var entry in chunk)
                    {
                        results[entry.Index] = new BlobBulkResult(entry.Location, Result<bool, Exception>.Fail(e));
                    }
                }
            }
        }

        return results;
    }

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

    public async ValueTask<int> DeleteAllAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(query);

        // Resolve the container + prefix through the single seam (the prefix was already path-security validated at
        // BlobQuery construction), so delete-by-prefix cannot escape into traversal or an un-normalized container.
        var (azureContainer, prefix) = BlobLocationResolver.ResolveQuery(query, normalizer);
        var containerClient = blobServiceClient.GetBlobContainerClient(azureContainer);

        // Names only — DeleteAll does not need metadata, just the container-relative keys to bulk-delete.
        var pages = containerClient
            .GetBlobsAsync(
                traits: BlobTraits.None,
                states: BlobStates.None,
                prefix: prefix,
                cancellationToken: cancellationToken
            )
            .AsPages(pageSizeHint: query.PageSize);

        var count = 0;

        await using var enumerator = pages.GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }
                }
                catch (RequestFailedException e)
                    when (e.Status == 404 && string.Equals(e.ErrorCode, "ContainerNotFound", StringComparison.Ordinal))
                {
                    // A missing container means there is nothing to delete.
                    break;
                }

                var names = enumerator.Current.Values.Select(static blob => blob.Name).ToArray();

                if (names.Length == 0)
                {
                    continue;
                }

                // Listed names are already container-relative keys; bulk-delete them against the same container.
                var deleteResults = await BulkDeleteAsync(query.Container, names, cancellationToken)
                    .ConfigureAwait(false);

                count += deleteResults.Count(static result => result.Result is { IsSuccess: true, Value: true });
            }
        }
        catch (Exception e)
        {
            // Surface the partial progress before propagating so a mid-enumeration failure is not silent.
            logger.LogDeleteAllPartialFailure(e, count, azureContainer, prefix);

            throw;
        }

        logger.LogFinishedDeletingFiles(count, azureContainer, prefix);

        return count;
    }

    #endregion

    #region Move / Copy

    public async ValueTask<bool> CopyAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    )
    {
        var (oldContainer, oldKey) = BlobLocationResolver.Resolve(source, normalizer);
        var (newContainer, newKey) = BlobLocationResolver.Resolve(destination, normalizer);

        var oldBlobClient = blobServiceClient.GetBlobContainerClient(oldContainer).GetBlobClient(oldKey);
        var newBlobClient = blobServiceClient.GetBlobContainerClient(newContainer).GetBlobClient(newKey);

        try
        {
            var copyResult = await newBlobClient
                .StartCopyFromUriAsync(oldBlobClient.Uri, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // WaitForCompletionAsync returns only once the copy has completed (otherwise it throws), so reaching
            // here means the copy succeeded.
            await copyResult.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            // A missing source blob OR container yields a graceful failure (false), not an exception: the
            // cross-provider conformance contract requires copy/move against a missing source to not throw.
            return false;
        }
    }

    public async ValueTask<bool> MoveAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    )
    {
        // Non-atomic copy-then-delete with best-effort destination rollback if deleting the source fails.
        if (!await CopyAsync(source, destination, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        bool deleted;

        try
        {
            deleted = await DeleteAsync(source, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The copy already succeeded but deleting the source threw (e.g. cancellation or a transient error);
            // roll back the copy so we never leave both source and destination behind.
            await _RollbackMoveCopyAsync(destination).ConfigureAwait(false);

            throw;
        }

        if (!deleted)
        {
            await _RollbackMoveCopyAsync(destination).ConfigureAwait(false);

            return false;
        }

        return true;
    }

    // Best-effort rollback of a move's copy. Uses CancellationToken.None so cleanup still runs even when the move
    // was cancelled, and swallows (logs) failures so the caller still observes the original outcome/exception rather
    // than a rollback error.
    private async Task _RollbackMoveCopyAsync(BlobLocation destination)
    {
        try
        {
            await DeleteAsync(destination, CancellationToken.None).ConfigureAwait(false);
            logger.LogMoveFailedRolledBack(destination.ToString());
        }
        catch (Exception e)
        {
            logger.LogMoveRollbackFailed(e, destination.ToString());
        }
    }

    #endregion

    #region Exists

    public async ValueTask<bool> ExistsAsync(BlobLocation location, CancellationToken cancellationToken = default)
    {
        var (container, key) = BlobLocationResolver.Resolve(location, normalizer);
        var blobClient = blobServiceClient.GetBlobContainerClient(container).GetBlobClient(key);

        var response = await blobClient.ExistsAsync(cancellationToken).ConfigureAwait(false);

        return response.Value;
    }

    #endregion

    #region Download / Info

    public async ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
        var (container, key) = BlobLocationResolver.Resolve(location, normalizer);
        var blobClient = blobServiceClient.GetBlobContainerClient(container).GetBlobClient(key);

        try
        {
            // Fetch metadata (and confirm existence) first so the download surfaces the caller's metadata like the
            // other providers; the framework uploadDate/extension keys are stripped. A missing blob/container 404s
            // here and maps to null below, before any stream is opened, so there is nothing to leak. Then stream
            // lazily from the network instead of buffering the whole blob into a MemoryStream (which OOMs on large
            // blobs). The caller owns the returned result and disposes the stream (see IBlobStorage.OpenReadStreamAsync).
            var properties = await blobClient
                .GetPropertiesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var metadata = BlobStorageHelpers.ToUserMetadata(_ToMetadata(properties.Value.Metadata));

            var stream = await blobClient.OpenReadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            return new(stream, location.Path, metadata);
        }
        catch (RequestFailedException e)
            when (e.ErrorCode == BlobErrorCode.BlobNotFound || e.ErrorCode == BlobErrorCode.ContainerNotFound)
        {
            return null;
        }
    }

    public async ValueTask<BlobInfo?> GetBlobInfoAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
        // M3 fold: argument validation is owned by the BlobLocation constructor, so there is no IsNotNull vs
        // IsNotNullOrEmpty divergence between this method and the rest of the surface.
        var (container, key) = BlobLocationResolver.Resolve(location, normalizer);
        var blobClient = blobServiceClient.GetBlobContainerClient(container).GetBlobClient(key);

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

        var properties = blobProperties.Value;

        return new BlobInfo
        {
            BlobKey = key,
            Size = properties.ContentLength,
            Created = properties.CreatedOn,
            Modified = properties.LastModified,
            // M2 fold: the GetProperties response carries the per-blob metadata; surface the caller's keys only
            // (the framework uploadDate/extension keys are stripped). The list path strips identically, so the two
            // stay consistent.
            Metadata = BlobStorageHelpers.ToUserMetadata(_ToMetadata(properties.Metadata)),
        };
    }

    #endregion

    #region List

    public async ValueTask<BlobPage> ListAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(query);

        var (container, prefix) = BlobLocationResolver.ResolveQuery(query, normalizer);
        var containerClient = blobServiceClient.GetBlobContainerClient(container);

        // Request one native Azure page sized to PageSize, resuming from the opaque continuation token when present.
        var pages = containerClient
            .GetBlobsAsync(
                traits: BlobTraits.Metadata,
                states: BlobStates.None,
                prefix: prefix,
                cancellationToken: cancellationToken
            )
            .AsPages(query.ContinuationToken, query.PageSize);

        await using var enumerator = pages.GetAsyncEnumerator(cancellationToken);

        try
        {
            if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                return BlobPage.Empty;
            }
        }
        catch (RequestFailedException e)
            when (e.Status == 404 && string.Equals(e.ErrorCode, "ContainerNotFound", StringComparison.Ordinal))
        {
            // A missing container lists as empty rather than throwing.
            return BlobPage.Empty;
        }

        var page = enumerator.Current;
        var items = new List<BlobInfo>(page.Values.Count);

        foreach (var blobItem in page.Values)
        {
            items.Add(_ToBlobInfo(blobItem));
        }

        // Pass Azure's native ContinuationToken straight through as the opaque BlobPage token; an empty/null token
        // marks the last page. The token is round-tripped by callers into a new BlobQuery.
        var continuationToken = string.IsNullOrEmpty(page.ContinuationToken) ? null : page.ContinuationToken;

        return new BlobPage(items, continuationToken);
    }

    // Projects an Azure BlobItem into the provider-agnostic BlobInfo, including any list-returned metadata. Zero-byte
    // blobs are intentionally NOT filtered out — an empty blob is a real, listable object on Azure.
    private static BlobInfo _ToBlobInfo(BlobItem blobItem)
    {
        return new BlobInfo
        {
            BlobKey = blobItem.Name,
            Size = blobItem.Properties.ContentLength ?? 0,
            Created = blobItem.Properties.CreatedOn ?? DateTimeOffset.MinValue,
            Modified = blobItem.Properties.LastModified ?? DateTimeOffset.MinValue,
            Metadata = BlobStorageHelpers.ToUserMetadata(_ToMetadata(blobItem.Metadata)),
        };
    }

    // Converts an Azure metadata dictionary (non-null string values) into the contract's read-only shape, or null
    // when empty so BlobInfo.Metadata stays absent rather than an empty dictionary.
    private static IReadOnlyDictionary<string, string>? _ToMetadata(IDictionary<string, string>? metadata)
    {
        if (metadata is not { Count: > 0 })
        {
            return null;
        }

        return new Dictionary<string, string>(metadata, StringComparer.Ordinal);
    }

    #endregion

    #region Presigned Urls

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expiry"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the <see cref="BlobServiceClient"/> cannot generate a SAS URI (no account key or user-delegation credentials).</exception>
    public ValueTask<Uri> GetPresignedDownloadUrlAsync(
        BlobLocation location,
        TimeSpan expiry,
        CancellationToken cancellationToken = default
    )
    {
        return _GetPresignedUrlAsync(location, expiry, BlobSasPermissions.Read, cancellationToken);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expiry"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the <see cref="BlobServiceClient"/> cannot generate a SAS URI (no account key or user-delegation credentials).</exception>
    public ValueTask<Uri> GetPresignedUploadUrlAsync(
        BlobLocation location,
        TimeSpan expiry,
        CancellationToken cancellationToken = default
    )
    {
        return _GetPresignedUrlAsync(
            location,
            expiry,
            BlobSasPermissions.Create | BlobSasPermissions.Write,
            cancellationToken
        );
    }

    /// <summary>Builds a SAS-signed presigned URL for a blob with the given permissions and expiry.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expiry"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the <see cref="BlobServiceClient"/> was not built with signing credentials and cannot generate a SAS URI.
    /// </exception>
    private async ValueTask<Uri> _GetPresignedUrlAsync(
        BlobLocation location,
        TimeSpan expiry,
        BlobSasPermissions permissions,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsPositive(expiry);

        // Route the validated location through the same BlobLocation seam as the data plane so the container/key are
        // validated and normalized identically.
        var (azureContainer, key) = BlobLocationResolver.Resolve(location, normalizer);

        var blobClient = blobServiceClient.GetBlobContainerClient(azureContainer).GetBlobClient(key);

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

    #region Dispose

    private bool _disposed;

    public ValueTask DisposeAsync()
    {
        // The keyed IPresignedUrlBlobStorage forward and the keyed IBlobStorage resolve to this same instance, so
        // the DI container tracks it twice and disposes it twice — guard so the dispose path stays idempotent. The
        // BlobServiceClient is owned by DI / the caller's clientFactory, not by this engine, so it is not disposed.
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        return ValueTask.CompletedTask;
    }

    #endregion
}

internal static partial class AzureBlobStorageLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "MoveFailedRolledBack",
        Level = LogLevel.Warning,
        Message = "Move failed for {Blob}, rolled back the destination copy"
    )]
    public static partial void LogMoveFailedRolledBack(this ILogger logger, string blob);

    [LoggerMessage(
        EventId = 2,
        EventName = "MoveRollbackFailed",
        Level = LogLevel.Error,
        Message = "Move rollback failed for {Blob}: could not delete the copied blob; a duplicate may remain"
    )]
    public static partial void LogMoveRollbackFailed(this ILogger logger, Exception exception, string blob);

    [LoggerMessage(
        EventId = 3,
        EventName = "FinishedDeletingFiles",
        Level = LogLevel.Trace,
        Message = "Finished deleting {FileCount} files in {Container} under prefix {Prefix}"
    )]
    public static partial void LogFinishedDeletingFiles(
        this ILogger logger,
        int fileCount,
        string container,
        string? prefix
    );

    [LoggerMessage(
        EventId = 4,
        EventName = "DeleteAllPartialFailure",
        Level = LogLevel.Error,
        Message = "DeleteAllAsync failed after deleting {DeletedCount} files in {Container} under prefix {Prefix}"
    )]
    public static partial void LogDeleteAllPartialFailure(
        this ILogger logger,
        Exception exception,
        int deletedCount,
        string container,
        string? prefix
    );
}
