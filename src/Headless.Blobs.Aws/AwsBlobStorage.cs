// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Abstractions;
using Headless.Blobs.Internals;
using Headless.Checks;
using Headless.IO;
using Headless.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Blobs.Aws;

/// <summary>
/// <see cref="IBlobStorage"/> implementation backed by Amazon S3 (or any S3-compatible endpoint).
/// Also implements <see cref="IPresignedUrlBlobStorage"/> using AWS Signature Version 4 (SigV4) pre-signing.
/// </summary>
/// <remarks>
/// Container/bucket lifecycle (create/exists/delete) is intentionally <b>not</b> implemented here — it lives on the
/// separately-registered <see cref="IBlobContainerManager"/> capability (<see cref="AwsBlobContainerManager"/>). This
/// type is reused as-is by the Cloudflare R2 provider, which cannot manage buckets; keeping bucket lifecycle off this
/// type is what lets R2 stay honestly capability-less. <see cref="UploadAsync"/> never auto-creates a missing bucket.
/// </remarks>
public sealed class AwsBlobStorage(
    IAmazonS3 s3,
    IMimeTypeProvider mimeTypeProvider,
    IClock clock,
    IOptions<AwsBlobStorageOptions> optionsAccessor,
    IBlobNamingNormalizer normalizer,
    ILogger<AwsBlobStorage>? logger = null
) : IBlobStorage, IPresignedUrlBlobStorage
{
    private const string _DefaultCacheControl = "must-revalidate, max-age=7776000";
    private const string _MetaDataHeaderPrefix = "x-amz-meta-";

    private readonly AwsBlobStorageOptions _options = optionsAccessor.Value;
    private readonly ILogger _logger = logger ?? NullLogger<AwsBlobStorage>.Instance;

    #region Upload

    public async ValueTask UploadAsync(
        BlobLocation location,
        Stream content,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(content);

        var (bucket, objectKey) = BlobLocationResolver.Resolve(location, normalizer);

        Stream inputStream;
        var ownsStream = false;

        if (content.CanSeek)
        {
            content.Position = 0;
            inputStream = content;
        }
        else
        {
            // S3 needs a known length, so non-seekable content is buffered to memory before upload.
            var streamCopy = new MemoryStream();
            await content.CopyToAsync(streamCopy, cancellationToken).ConfigureAwait(false);
            streamCopy.Position = 0;
            inputStream = streamCopy;
            ownsStream = true;
        }

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = objectKey,
            InputStream = inputStream,
            AutoCloseStream = ownsStream,
            AutoResetStreamPosition = false,
            ContentType = mimeTypeProvider.GetMimeType(location.Path),
            UseChunkEncoding = _options.UseChunkEncoding,
            CannedACL = _options.CannedAcl,
            Headers = { CacheControl = _DefaultCacheControl },
            DisablePayloadSigning = _options.DisablePayloadSigning,
        };

        // Copy the caller's metadata into the S3 request (never mutate the caller's dictionary), then layer the
        // framework keys on top so they are always present regardless of what the caller supplied.
        if (metadata is not null)
        {
            foreach (var pair in metadata)
            {
                // MetadataCollection automatically prefixes keys with "x-amz-meta-".
                request.Metadata[pair.Key] = pair.Value;
            }
        }

        request.Metadata[BlobStorageHelpers.UploadDateMetadataKey] = clock.UtcNow.ToString("O");
        request.Metadata[BlobStorageHelpers.ExtensionMetadataKey] = Path.GetExtension(location.Path);

        var response = await s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);

        response.HttpStatusCode.EnsureSuccessStatusCode();
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
            MaxDegreeOfParallelism = _options.MaxBulkParallelism,
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

                    // Build the per-item location inside the try so an unaddressable key (traversal, reserved
                    // sidecar suffix, etc.) becomes a per-item failure instead of aborting the whole batch.
                    var location = default(BlobLocation);

                    try
                    {
                        location = new BlobLocation(container, blob.Path);
                        await UploadAsync(location, blob.Stream, blob.Metadata, ct).ConfigureAwait(false);
                        results[i] = new BlobBulkResult(location, Result<bool, Exception>.Ok(true));
                    }
                    catch (Exception e)
                    {
                        results[i] = new BlobBulkResult(location, Result<bool, Exception>.Fail(e));
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
        var (bucket, objectKey) = BlobLocationResolver.Resolve(location, normalizer);

        return await _DeleteResolvedAsync(bucket, objectKey, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> _DeleteResolvedAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken
    )
    {
        if (!await _ExistsAsync(bucket, objectKey, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var request = new DeleteObjectRequest { BucketName = bucket, Key = objectKey };

        DeleteObjectResponse response;

        try
        {
            response = await s3.DeleteObjectAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            return true;
        }

        response.HttpStatusCode.EnsureSuccessStatusCode();

        return true;
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

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxBulkParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel
            .ForAsync(
                0,
                items.Count,
                options,
                async (i, ct) =>
                {
                    var path = items[i];

                    // H1 fold: build the location (validates) and resolve the bucket + key through the single seam,
                    // so a bulk delete can never target an un-normalized bucket or a raw, un-validated key. An
                    // unaddressable key fails that one item without aborting the batch.
                    var location = default(BlobLocation);

                    try
                    {
                        location = new BlobLocation(container, path);
                        var (bucket, key) = BlobLocationResolver.Resolve(location, normalizer);
                        var deleted = await _DeleteResolvedAsync(bucket, key, ct).ConfigureAwait(false);
                        results[i] = new BlobBulkResult(location, Result<bool, Exception>.Ok(deleted));
                    }
                    catch (Exception e)
                    {
                        results[i] = new BlobBulkResult(location, Result<bool, Exception>.Fail(e));
                    }
                }
            )
            .ConfigureAwait(false);

        return results;
    }

    public async ValueTask<int> DeleteAllAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(query);

        // Resolve the bucket + prefix through the single seam (the prefix was already path-security validated at
        // BlobQuery construction), so delete-by-prefix cannot escape into traversal or an un-normalized bucket.
        var (bucket, prefix) = BlobLocationResolver.ResolveQuery(query, normalizer);

        var listRequest = new ListObjectsV2Request
        {
            BucketName = bucket,
            Prefix = prefix,
            MaxKeys = query.PageSize,
        };

        var errors = new List<DeleteError>();
        var count = 0;

        ListObjectsV2Response listResponse;

        do
        {
            try
            {
                listResponse = await s3.ListObjectsV2Async(listRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
            {
                return count; // A missing bucket means there is nothing to delete.
            }

            // Paginate via the native S3 continuation token.
            listRequest.ContinuationToken = listResponse.NextContinuationToken;

            var keys = (listResponse.S3Objects ?? [])
                .Where(o => o is not null)
                .Select(o => new KeyVersion { Key = o.Key })
                .ToArray();

            if (keys.Length == 0)
            {
                continue;
            }

            var deleteRequest = new DeleteObjectsRequest
            {
                BucketName = bucket,
                Objects = [.. keys],
                Quiet = false,
            };

            _logger.LogDeletingFiles(keys.Length, prefix);

            var deleteResponse = await s3.DeleteObjectsAsync(deleteRequest, cancellationToken).ConfigureAwait(false);

            if (deleteResponse.DeleteErrors?.Count > 0)
            {
                // Retry the failed keys once, then surface anything still failing.
                var retryObjects = deleteResponse.DeleteErrors.ConvertAll(e => new KeyVersion { Key = e.Key });
                var retryRequest = new DeleteObjectsRequest { BucketName = bucket, Objects = retryObjects };

                var retryResponse = await s3.DeleteObjectsAsync(retryRequest, cancellationToken).ConfigureAwait(false);

                // Objects deleted only on retry must be counted too; the contract returns the number actually deleted.
                count += retryResponse.DeletedObjects?.Count ?? 0;

                if (retryResponse.DeleteErrors?.Count > 0)
                {
                    errors.AddRange(retryResponse.DeleteErrors);
                }
            }

            _logger.LogDeletedFiles(deleteResponse.DeletedObjects?.Count ?? 0, prefix);

            count += deleteResponse.DeletedObjects?.Count ?? 0;
        } while (listResponse.IsTruncated is true && !cancellationToken.IsCancellationRequested);

        if (errors.Count > 0)
        {
            var more = errors.Count > 20 ? errors.Count - 20 : 0;
            var keys = string.Join(',', errors.Take(20).Select(e => e.Key));

            throw new InvalidOperationException(
                $"Unable to delete all S3 entries \"{keys}\"{(more > 0 ? $" plus {more.ToString(CultureInfo.InvariantCulture)} more" : "")}."
            );
        }

        _logger.LogFinishedDeletingFiles(count, prefix);

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
        var (oldBucket, oldKey) = BlobLocationResolver.Resolve(source, normalizer);
        var (newBucket, newKey) = BlobLocationResolver.Resolve(destination, normalizer);

        var request = new CopyObjectRequest
        {
            CannedACL = _options.CannedAcl,
            SourceBucket = oldBucket,
            SourceKey = oldKey,
            DestinationBucket = newBucket,
            DestinationKey = newKey,
            MetadataDirective = S3MetadataDirective.COPY,
        };

        CopyObjectResponse response;

        try
        {
            response = await s3.CopyObjectAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }

        return response.HttpStatusCode.IsSuccessStatusCode();
    }

    public async ValueTask<bool> MoveAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    )
    {
        // Non-atomic copy-then-delete with best-effort destination rollback if the source delete fails.
        if (!await CopyAsync(source, destination, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var (oldBucket, oldKey) = BlobLocationResolver.Resolve(source, normalizer);

        var deleteRequest = new DeleteObjectRequest { BucketName = oldBucket, Key = oldKey };
        var deleteResponse = await s3.DeleteObjectAsync(deleteRequest, cancellationToken).ConfigureAwait(false);

        if (!deleteResponse.HttpStatusCode.IsSuccessStatusCode())
        {
            _logger.LogFailedToDeleteOriginalRollback(oldBucket, oldKey);

            // Compensating transaction: delete the copy to restore the original state.
            var (newBucket, newKey) = BlobLocationResolver.Resolve(destination, normalizer);
            var compensate = new DeleteObjectRequest { BucketName = newBucket, Key = newKey };
            await s3.DeleteObjectAsync(compensate, cancellationToken).ConfigureAwait(false);

            return false;
        }

        return true;
    }

    #endregion

    #region Exists

    public ValueTask<bool> ExistsAsync(BlobLocation location, CancellationToken cancellationToken = default)
    {
        var (bucket, key) = BlobLocationResolver.Resolve(location, normalizer);

        return _ExistsAsync(bucket, key, cancellationToken);
    }

    private async ValueTask<bool> _ExistsAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        GetObjectMetadataResponse? response;

        try
        {
            response = await s3.GetObjectMetadataAsync(bucket, key, cancellationToken).ConfigureAwait(false);
        }
        // A missing object or a missing bucket both surface as 404 here.
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }

        if (response.HttpStatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }

        response.HttpStatusCode.EnsureSuccessStatusCode();

        return true;
    }

    #endregion

    #region Download / Info

    public async ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
        var (bucket, key) = BlobLocationResolver.Resolve(location, normalizer);

        var request = new GetObjectRequest { BucketName = bucket, Key = key };

        GetObjectResponse response;

        try
        {
            response = await s3.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
        }
        // A missing object or a missing bucket both surface as 404 here.
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.HttpStatusCode is HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        try
        {
            response.HttpStatusCode.EnsureSuccessStatusCode();
        }
        catch
        {
            response.Dispose();

            throw;
        }

        var stream = new ActionableStream(response.ResponseStream, response.Dispose);

        return new(stream, location.Path, BlobStorageHelpers.ToUserMetadata(_ToDictionary(response.Metadata)));
    }

    public async ValueTask<BlobInfo?> GetBlobInfoAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
        // M3 fold: argument validation is owned by the BlobLocation constructor, so there is no IsNotNull vs
        // IsNotNullOrEmpty divergence between this method and the rest of the surface.
        var (bucket, objectKey) = BlobLocationResolver.Resolve(location, normalizer);

        var request = new GetObjectMetadataRequest { BucketName = bucket, Key = objectKey };

        GetObjectMetadataResponse? response;

        try
        {
            response = await s3.GetObjectMetadataAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.HttpStatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        response.HttpStatusCode.EnsureSuccessStatusCode();

        var created = _GetUploadedDate(response.Metadata);
        var modified = response.LastModified is null ? created : new(response.LastModified.Value);

        return new BlobInfo
        {
            BlobKey = objectKey,
            Created = created,
            Modified = modified,
            Size = response.ContentLength,
            // M2 fold: the HEAD response carries the per-object metadata; surface the caller's keys only (the
            // framework uploadDate/extension keys are stripped). The list API cannot, which is why ListAsync leaves it null.
            Metadata = BlobStorageHelpers.ToUserMetadata(_ToDictionary(response.Metadata)),
        };
    }

    #endregion

    #region List

    public async ValueTask<BlobPage> ListAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(query);

        var (bucket, prefix) = BlobLocationResolver.ResolveQuery(query, normalizer);

        var request = new ListObjectsV2Request
        {
            BucketName = bucket,
            Prefix = prefix,
            MaxKeys = query.PageSize,
            ContinuationToken = query.ContinuationToken,
        };

        ListObjectsV2Response response;

        try
        {
            response = await s3.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            return BlobPage.Empty;
        }

        response.HttpStatusCode.EnsureSuccessStatusCode();

        var items = new List<BlobInfo>(response.S3Objects?.Count ?? 0);

        foreach (var s3Object in response.S3Objects ?? [])
        {
            if (s3Object is null)
            {
                continue;
            }

            var blobInfo = _ToBlobInfo(s3Object);

            // Filter directory placeholder keys (zero-byte objects whose key ends with '/').
            if (!_IsDirectory(blobInfo))
            {
                items.Add(blobInfo);
            }
        }

        // Pass S3's NextContinuationToken straight through as the opaque BlobPage token; null when not truncated.
        var continuationToken = response.IsTruncated is true ? response.NextContinuationToken : null;

        return new BlobPage(items, continuationToken);
    }

    private static BlobInfo _ToBlobInfo(S3Object blob)
    {
        var modified = blob.LastModified is null
            ? DateTimeOffset.MinValue
            : new DateTimeOffset(blob.LastModified.Value);

        return new BlobInfo
        {
            BlobKey = blob.Key,
            // L3 fold: the S3 ListObjectsV2 API does not return per-object user metadata, so the true upload date
            // (stored in object metadata) is unavailable here and Created falls back to LastModified. Call
            // GetBlobInfoAsync for the authoritative Created timestamp and metadata.
            Created = modified,
            Modified = modified,
            Size = blob.Size ?? 0,
            Metadata = null,
        };
    }

    private static bool _IsDirectory(BlobInfo file)
    {
        return file.Size is 0 && file.BlobKey.EndsWith('/');
    }

    #endregion

    #region Metadata Converters

    private static IReadOnlyDictionary<string, string>? _ToDictionary(MetadataCollection? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        var dictionary = new Dictionary<string, string>(metadata.Count, StringComparer.Ordinal);

        foreach (var awsMetadataKey in metadata.Keys)
        {
            var key = awsMetadataKey.StartsWith(_MetaDataHeaderPrefix, StringComparison.Ordinal)
                ? awsMetadataKey[_MetaDataHeaderPrefix.Length..]
                : awsMetadataKey;

            dictionary[key] = metadata[awsMetadataKey] ?? string.Empty;
        }

        return dictionary;
    }

    private static DateTimeOffset _GetUploadedDate(MetadataCollection? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return DateTimeOffset.MinValue;
        }

        var createdValue = metadata[BlobStorageHelpers.UploadDateMetadataKey];

        if (createdValue is null)
        {
            return DateTimeOffset.MinValue;
        }

        return DateTimeOffset.TryParseExact(
            createdValue,
            "o",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var value
        )
            ? value
            : DateTimeOffset.MinValue;
    }

    #endregion

    #region Presigned Urls

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expiry"/> is not positive.</exception>
    public ValueTask<Uri> GetPresignedDownloadUrlAsync(
        BlobLocation location,
        TimeSpan expiry,
        CancellationToken cancellationToken = default
    )
    {
        return _GetPresignedUrlAsync(location, expiry, HttpVerb.GET, cancellationToken);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expiry"/> is not positive.</exception>
    public ValueTask<Uri> GetPresignedUploadUrlAsync(
        BlobLocation location,
        TimeSpan expiry,
        CancellationToken cancellationToken = default
    )
    {
        return _GetPresignedUrlAsync(location, expiry, HttpVerb.PUT, cancellationToken);
    }

    private async ValueTask<Uri> _GetPresignedUrlAsync(
        BlobLocation location,
        TimeSpan expiry,
        HttpVerb verb,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsPositive(expiry);

        // Route the validated location through the same BlobLocation seam as the data plane so the bucket/key are
        // validated and normalized identically.
        var (bucket, key) = BlobLocationResolver.Resolve(location, normalizer);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Verb = verb,
            // SigV4 presigning is performed locally; Expires is the absolute deadline.
            Expires = clock.UtcNow.Add(expiry).UtcDateTime,
        };

        var url = await s3.GetPreSignedURLAsync(request).ConfigureAwait(false);

        return new Uri(url);
    }

    #endregion

    #region Dispose

    private bool _disposed;

    public ValueTask DisposeAsync()
    {
        // The keyed IPresignedUrlBlobStorage forward and the keyed IBlobStorage resolve to this same instance, so
        // the DI container tracks it twice and disposes it twice — guard so the dispose path stays idempotent.
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        // The S3 client is built per-store by the DI factory and handed to this engine to own (it is not a
        // container-tracked service), so this instance is responsible for releasing its HTTP handler/sockets.
        (s3 as IDisposable)?.Dispose();

        return ValueTask.CompletedTask;
    }

    #endregion
}

internal static partial class AwsBlobStorageLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "DeletingFiles",
        Level = LogLevel.Information,
        Message = "Deleting {FileCount} files under prefix {Prefix}"
    )]
    public static partial void LogDeletingFiles(this ILogger logger, int fileCount, string? prefix);

    [LoggerMessage(
        EventId = 2,
        EventName = "DeletedFiles",
        Level = LogLevel.Trace,
        Message = "Deleted {FileCount} files under prefix {Prefix}"
    )]
    public static partial void LogDeletedFiles(this ILogger logger, int fileCount, string? prefix);

    [LoggerMessage(
        EventId = 3,
        EventName = "FinishedDeletingFiles",
        Level = LogLevel.Trace,
        Message = "Finished deleting {FileCount} files under prefix {Prefix}"
    )]
    public static partial void LogFinishedDeletingFiles(this ILogger logger, int fileCount, string? prefix);

    [LoggerMessage(
        EventId = 4,
        EventName = "FailedToDeleteOriginalRollback",
        Level = LogLevel.Error,
        Message = "Failed to delete original object {OldBucket}/{OldKey} after copy, rolling back"
    )]
    public static partial void LogFailedToDeleteOriginalRollback(this ILogger logger, string oldBucket, string oldKey);
}
