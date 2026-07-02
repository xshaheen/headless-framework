// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Abstractions;
using Headless.Blobs.Internals;
using Headless.Checks;
using Headless.IO;
using Headless.Primitives;
using Headless.Threading;
using Headless.Urls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Blobs.Aws;

/// <summary>
/// <see cref="IBlobStorage"/> implementation backed by Amazon S3 (or any S3-compatible endpoint).
/// Also implements <see cref="IPresignedUrlBlobStorage"/> using AWS Signature Version 4 (SigV4) pre-signing.
/// </summary>
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

    // Buckets this instance has already ensured exist, so the HeadBucket/PutBucket round trip runs at most once
    // per bucket rather than on every upload/copy. A bucket is recorded only after a successful ensure, so a
    // failed ensure is naturally retried. The per-bucket lock serializes concurrent first-time ensures of the
    // same bucket into a single create while letting distinct buckets be ensured in parallel.
    private readonly ConcurrentDictionary<string, byte> _ensuredBuckets = new(StringComparer.Ordinal);
    private readonly KeyedAsyncLock _ensureBucketLock = new();

    #region Create Container

    public async ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(container);

        // Explicit creation always runs regardless of AutoCreateContainer; it also primes the per-instance cache.
        await _EnsureBucketOnceAsync(_BuildBucketName(container), cancellationToken).ConfigureAwait(false);
    }

    private async Task _EnsureBucketOnceAsync(string bucketName, CancellationToken cancellationToken)
    {
        if (_ensuredBuckets.ContainsKey(bucketName))
        {
            return;
        }

        using (await _ensureBucketLock.LockAsync(bucketName, cancellationToken).ConfigureAwait(false))
        {
            // Re-check under the lock: a concurrent caller may have ensured this bucket while we waited.
            if (_ensuredBuckets.ContainsKey(bucketName))
            {
                return;
            }

            await _CreateBucketAsync(bucketName, cancellationToken).ConfigureAwait(false);

            // Record only after a successful create so a failed ensure is retried next time.
            _ensuredBuckets.TryAdd(bucketName, 0);
        }
    }

    private async Task _CreateBucketAsync(string bucketName, CancellationToken cancellationToken)
    {
        // Idempotent, cancellation-aware create: PutBucket directly (it carries a CancellationToken, unlike the
        // DoesS3BucketExistV2Async HEAD probe) and treat "already owned by you" as success. The per-instance
        // cache means this runs at most once per bucket, so a separate existence pre-check is redundant.
        try
        {
            var request = new PutBucketRequest { BucketName = bucketName };
            await s3.PutBucketAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e)
            when (string.Equals(e.ErrorCode, "BucketAlreadyOwnedByYou", StringComparison.Ordinal))
        {
            // The bucket already exists and we own it.
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

        var (bucket, objectKey) = _BuildObjectKey(blobName, container);

        if (_options.AutoCreateContainer)
        {
            await _EnsureBucketOnceAsync(bucket, cancellationToken).ConfigureAwait(false);
        }

        Stream inputStream;
        var ownsStream = false;

        if (stream.CanSeek)
        {
            stream.Position = 0;
            inputStream = stream;
        }
        else
        {
            var streamCopy = new MemoryStream();
            await stream.CopyToAsync(streamCopy, cancellationToken).ConfigureAwait(false);
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
            ContentType = mimeTypeProvider.GetMimeType(blobName),
            UseChunkEncoding = _options.UseChunkEncoding,
            CannedACL = _options.CannedAcl,
            Headers = { CacheControl = _DefaultCacheControl },
            DisablePayloadSigning = _options.DisablePayloadSigning,
        };

        if (metadata is not null)
        {
            foreach (var m in metadata)
            {
                // Note: MetadataCollection automatically prefixed keys with "x-amz-meta-"
                request.Metadata[m.Key] = m.Value;
            }
        }

        request.Metadata[BlobStorageHelpers.UploadDateMetadataKey] = clock.UtcNow.ToString("O");
        request.Metadata[BlobStorageHelpers.ExtensionMetadataKey] = Path.GetExtension(blobName);

        var response = await s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);

        response.HttpStatusCode.EnsureSuccessStatusCode();
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

        // Index results by enumeration position, not the order parallel bodies start, so results[i] always
        // describes items[i] — honoring the "one Result per input blob, in original order" contract.
        var items = blobs.AsIReadOnlyList();
        var results = new Result<Exception>[items.Count];

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
        var (bucket, objectKey) = _BuildObjectKey(blobName, container);

        if (!await _ExistsAsync(bucket, objectKey, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var request = new DeleteObjectRequest { BucketName = bucket, Key = objectKey };

        DeleteObjectResponse? response;

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

        // Route through the same validated builder UploadAsync/DeleteAsync use: normalize the bucket and run
        // PathValidation on every container segment + blob name, instead of taking raw input.
        var bucket = _BuildBucketName(container);

        var objectKeys = blobNames
            .Select(blobName => new KeyVersion { Key = _BuildObjectKey(blobName, container).ObjectKey })
            .ToList();

        var request = new DeleteObjectsRequest
        {
            BucketName = bucket,
            Objects = objectKeys,
            Quiet = false,
        };

        DeleteObjectsResponse response;

        try
        {
            response = await s3.DeleteObjectsAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            return objectKeys.ConvertAll(_ => Result<bool, Exception>.Ok(value: true));
        }
        catch (DeleteObjectsException e) // This exception is thrown when some items fail to delete.
        {
            var results = new List<Result<bool, Exception>>(blobNames.Count);

            foreach (var objectKey in objectKeys)
            {
                var deleteError = e.Response.DeleteErrors?.Find(x =>
                    string.Equals(x.Key, objectKey.Key, StringComparison.Ordinal)
                );

                if (deleteError is not null)
                {
                    var exception = new InvalidOperationException(
                        $"Error deleting item with Code: {deleteError.Code} and Message: {deleteError.Message}"
                    );

                    results.Add(Result<bool, Exception>.Fail(exception));
                }
                else
                {
                    results.Add(Result<bool, Exception>.Ok(value: true));
                }
            }

            return results;
        }

        response.HttpStatusCode.EnsureSuccessStatusCode();

        // No exceptions were thrown, so all items were deleted successfully.

        return objectKeys.ConvertAll(_ => Result<bool, Exception>.Ok(value: true));
    }

    public async ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);

        const int pageSize = 100;

        // Normalize + validate the bucket via the shared builder (parity with GetBlobsAsync) instead of raw container[0].
        var bucket = _BuildBucketName(container);
        var criteria = BlobStorageHelpers.GetRequestCriteria(container.Skip(1), blobSearchPattern);

        var listRequest = new ListObjectsV2Request
        {
            BucketName = bucket,
            Prefix = criteria.Prefix,
            MaxKeys = pageSize,
        };

        var deleteRequest = new DeleteObjectsRequest { BucketName = bucket };

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
                return 0; // If the bucket doesn't exist, there are no files to delete.
            }

            listRequest.ContinuationToken = listResponse.NextContinuationToken;

            var keys = _MatchesPattern(listResponse.S3Objects, criteria.Pattern)
                .Select(o => new KeyVersion { Key = o.Key })
                .ToArray();

            if (keys.Length == 0)
            {
                continue;
            }

            deleteRequest.Objects ??= [];
            deleteRequest.Objects.AddRange(keys);

            _logger.LogDeletingFiles(keys.Length, blobSearchPattern);

            var deleteResponse = await s3.DeleteObjectsAsync(deleteRequest, cancellationToken).ConfigureAwait(false);

            if (deleteResponse.DeleteErrors?.Count > 0)
            {
                // retry 1 time, continue.
                var objects = deleteResponse.DeleteErrors.ConvertAll(e => new KeyVersion { Key = e.Key });
                var deleteRetryRequest = new DeleteObjectsRequest { BucketName = bucket, Objects = objects };

                var deleteRetryResponse = await s3.DeleteObjectsAsync(deleteRetryRequest, cancellationToken)
                    .ConfigureAwait(false);

                // Objects deleted only on retry must be counted too; the contract returns the number actually deleted.
                count += deleteRetryResponse.DeletedObjects?.Count ?? 0;

                if (deleteRetryResponse.DeleteErrors?.Count > 0)
                {
                    errors.AddRange(deleteRetryResponse.DeleteErrors);
                }
            }

            _logger.LogDeletedFiles(deleteResponse.DeletedObjects?.Count ?? 0, blobSearchPattern);

            count += deleteResponse.DeletedObjects?.Count ?? 0;
            deleteRequest.Objects?.Clear();
        } while (listResponse.IsTruncated is true && !cancellationToken.IsCancellationRequested);

        if (errors.Count > 0)
        {
            var more = errors.Count > 20 ? errors.Count - 20 : 0;
            var keys = string.Join(',', errors.Take(20).Select(e => e.Key));

            throw new InvalidOperationException(
                $"Unable to delete all S3 entries \"{keys}\"{(more > 0 ? $" plus {more.ToString(CultureInfo.InvariantCulture)} more" : "")}."
            );
        }

        _logger.LogFinishedDeletingFiles(count, blobSearchPattern);

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

        var (oldBucket, oldKey) = _BuildObjectKey(blobName, blobContainer);
        var (newBucket, newKey) = _BuildObjectKey(newBlobName, newBlobContainer);

        // Ensure new bucket exists (once per bucket per instance) when auto-create is enabled.
        if (_options.AutoCreateContainer)
        {
            await _EnsureBucketOnceAsync(newBucket, cancellationToken).ConfigureAwait(false);
        }

        var request = new CopyObjectRequest
        {
            CannedACL = _options.CannedAcl,
            SourceBucket = oldBucket,
            SourceKey = oldKey,
            DestinationBucket = newBucket,
            DestinationKey = newKey,
            MetadataDirective = S3MetadataDirective.COPY,
        };

        CopyObjectResponse? response;

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
        if (
            !await CopyAsync(blobContainer, blobName, newBlobContainer, newBlobName, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            return false;
        }

        var (oldBucket, oldKey) = _BuildObjectKey(blobName, blobContainer);
        var (newBucket, newKey) = _BuildObjectKey(newBlobName, newBlobContainer);

        var deleteRequest = new DeleteObjectRequest { BucketName = oldBucket, Key = oldKey };
        var deleteResponse = await s3.DeleteObjectAsync(deleteRequest, cancellationToken).ConfigureAwait(false);

        if (!deleteResponse.HttpStatusCode.IsSuccessStatusCode())
        {
            _logger.LogFailedToDeleteOriginalRollback(oldBucket, oldKey);

            // Compensating transaction: delete the copy to restore original state
            var compensate = new DeleteObjectRequest { BucketName = newBucket, Key = newKey };
            await s3.DeleteObjectAsync(compensate, cancellationToken).ConfigureAwait(false);

            return false;
        }

        return true;
    }

    #endregion

    #region Exists

    public ValueTask<bool> ExistsAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var (bucket, key) = _BuildObjectKey(blobName, container);

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

    #region Download

    public async ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var (bucket, key) = _BuildObjectKey(blobName, container);

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

        return new(stream, blobName, _ToDictionary(response.Metadata));
    }

    public async ValueTask<BlobInfo?> GetBlobInfoAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        var (bucket, objectKey) = _BuildObjectKey(blobName, container);

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

        var bucket = _BuildBucketName(container);
        var criteria = BlobStorageHelpers.GetRequestCriteria(container.Skip(1), blobSearchPattern);
        string? continuationToken = null;

        do
        {
            var req = new ListObjectsV2Request
            {
                BucketName = bucket,
                MaxKeys = 1000,
                Prefix = criteria.Prefix,
                ContinuationToken = continuationToken,
            };

            ListObjectsV2Response response;

            try
            {
                response = await s3.ListObjectsV2Async(req, cancellationToken).ConfigureAwait(false);
            }
            catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
            {
                yield break;
            }

            foreach (var s3Object in _MatchesPattern(response.S3Objects, criteria.Pattern))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var blobInfo = _ToBlobInfo(s3Object);

                if (!_IsDirectory(blobInfo))
                {
                    yield return blobInfo;
                }
            }

            continuationToken = response.IsTruncated is true ? response.NextContinuationToken : null;
        } while (continuationToken is not null);
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

        var bucket = _BuildBucketName(container);
        var criteria = BlobStorageHelpers.GetRequestCriteria(container.Skip(1), blobSearchPattern);

        var result = new PagedFileListResult(
            (_, token) => _GetFilesAsync(bucket, criteria, pageSize, continuationToken: null, token)
        );

        await result.NextPageAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    private async ValueTask<INextPageResult> _GetFilesAsync(
        string bucket,
        SearchCriteria criteria,
        int pageSize,
        string? continuationToken = null,
        CancellationToken cancellationToken = default
    )
    {
        var req = new ListObjectsV2Request
        {
            BucketName = bucket,
            MaxKeys = pageSize,
            Prefix = criteria.Prefix,
            ContinuationToken = continuationToken,
        };

        _logger.LogGettingFileList(criteria.Prefix, criteria.Pattern);

        ListObjectsV2Response? response;

        try
        {
            response = await s3.ListObjectsV2Async(req, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            return new NextPageResult
            {
                Success = true,
                HasMore = false,
                Blobs = [],
                NextPageFunc = null,
            };
        }

        var hasMore = response.IsTruncated is true;

        return new NextPageResult
        {
            Success = response.HttpStatusCode.IsSuccessStatusCode(),
            HasMore = hasMore,
            Blobs = _MatchesPattern(response.S3Objects, criteria.Pattern)
                .Select(_ToBlobInfo)
                .Where(spec => !_IsDirectory(spec))
                .ToList(),
            NextPageFunc = hasMore
                ? (_, token) => _GetFilesAsync(bucket, criteria, pageSize, response.NextContinuationToken, token)
                : null,
        };
    }

    private static BlobInfo _ToBlobInfo(S3Object blob)
    {
        var modified = blob.LastModified is null ? DateTimeOffset.MinValue : new(blob.LastModified.Value);

        return new()
        {
            BlobKey = blob.Key,
            // NOTE: The correct one is stored in the metadata collection, and it is not available here.
            Created = modified,
            Modified = modified,
            Size = blob.Size ?? 0,
        };
    }

    private static bool _IsDirectory(BlobInfo file)
    {
        return file.Size is 0 && file.BlobKey.EndsWith('/');
    }

    private static IEnumerable<S3Object> _MatchesPattern(IEnumerable<S3Object?>? blobs, Regex? pattern)
    {
        if (blobs is null)
        {
            return [];
        }

        return blobs.Where(blob =>
        {
            var path = blob?.Key;

            return path is not null && pattern?.IsMatch(path) != false;
        })!;
    }

    #endregion

    #region Build Urls

    private (string Bucket, string ObjectKey) _BuildObjectKey(string blobName, string[] container)
    {
        Argument.IsNotNullOrWhiteSpace(blobName);
        Argument.IsNotNullOrEmpty(container);

        PathValidation.ValidateContainer(container);
        PathValidation.ValidatePathSegment(blobName);

        // Two-tier naming: the first segment is the bucket (strict S3/R2 bucket rules); the remaining segments and
        // the blob name form the object key (lenient path-segment rules).
        var bucket = normalizer.NormalizeContainerName(container[0]);
        var objectKey = Url.Combine([
            .. container.Skip(1).Select(normalizer.NormalizeBlobName),
            normalizer.NormalizeBlobName(blobName),
        ]);

        return (bucket, objectKey);
    }

    private string _BuildBucketName(string[] container)
    {
        PathValidation.ValidateContainer(container);
        return normalizer.NormalizeContainerName(container[0]);
    }

    #endregion

    #region Metadata Converters

    private static Dictionary<string, string?>? _ToDictionary(MetadataCollection? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        var dictionary = new Dictionary<string, string?>(metadata.Count, StringComparer.Ordinal);

        foreach (var awsMetadataKey in metadata.Keys)
        {
            var key = awsMetadataKey.StartsWith(_MetaDataHeaderPrefix, StringComparison.Ordinal)
                ? awsMetadataKey[_MetaDataHeaderPrefix.Length..]
                : awsMetadataKey;

            dictionary[key] = metadata[awsMetadataKey];
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
    /// <exception cref="ArgumentException">Thrown when <paramref name="blobName"/> or <paramref name="container"/> fails validation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expiry"/> is not positive.</exception>
    public ValueTask<Uri> GetPresignedDownloadUrlAsync(
        string[] container,
        string blobName,
        TimeSpan expiry,
        CancellationToken cancellationToken = default
    )
    {
        return _GetPresignedUrlAsync(container, blobName, expiry, HttpVerb.GET, cancellationToken);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="blobName"/> or <paramref name="container"/> fails validation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expiry"/> is not positive.</exception>
    public ValueTask<Uri> GetPresignedUploadUrlAsync(
        string[] container,
        string blobName,
        TimeSpan expiry,
        CancellationToken cancellationToken = default
    )
    {
        return _GetPresignedUrlAsync(container, blobName, expiry, HttpVerb.PUT, cancellationToken);
    }

    private async ValueTask<Uri> _GetPresignedUrlAsync(
        string[] container,
        string blobName,
        TimeSpan expiry,
        HttpVerb verb,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsPositive(expiry);

        var (bucket, key) = _BuildObjectKey(blobName, container);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Verb = verb,
            // SigV4 presigning is performed locally; Expires is the absolute deadline.
            Expires = clock.UtcNow.Add(expiry).UtcDateTime,
        };

        // Honor the configured endpoint scheme. When the client targets a plaintext http:// endpoint (an
        // S3-compatible emulator such as LocalStack/MinIO), the SDK would otherwise rewrite the signed URL
        // scheme to https:// and the request would fail against the http endpoint. Real S3 is https, so the
        // default HTTPS is preserved there.
        if (
            s3.Config.ServiceURL is { Length: > 0 } serviceUrl
            && serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        )
        {
            request.Protocol = Protocol.HTTP;
        }

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
        _ensureBucketLock.Dispose();

        // The S3 client is built per-store by the DI factory and handed to this engine to own (it is not a
        // container-tracked service), so this instance is responsible for releasing its HTTP handler/sockets.
        s3?.Dispose();

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
        Message = "Deleting {FileCount} files matching {SearchPattern}"
    )]
    public static partial void LogDeletingFiles(this ILogger logger, int fileCount, string? searchPattern);

    [LoggerMessage(
        EventId = 2,
        EventName = "DeletedFiles",
        Level = LogLevel.Trace,
        Message = "Deleted {FileCount} files matching {SearchPattern}"
    )]
    public static partial void LogDeletedFiles(this ILogger logger, int fileCount, string? searchPattern);

    [LoggerMessage(
        EventId = 3,
        EventName = "FinishedDeletingFiles",
        Level = LogLevel.Trace,
        Message = "Finished deleting {FileCount} files matching {SearchPattern}"
    )]
    public static partial void LogFinishedDeletingFiles(this ILogger logger, int fileCount, string? searchPattern);

    [LoggerMessage(
        EventId = 4,
        EventName = "FailedToDeleteOriginalRollback",
        Level = LogLevel.Error,
        Message = "Failed to delete original object {OldBucket}/{OldKey} after copy, rolling back"
    )]
    public static partial void LogFailedToDeleteOriginalRollback(this ILogger logger, string oldBucket, string oldKey);

    [LoggerMessage(
        EventId = 5,
        EventName = "GettingFileList",
        Level = LogLevel.Trace,
        Message = "Getting file list matching {Prefix} and {Pattern}..."
    )]
    public static partial void LogGettingFileList(this ILogger logger, string? prefix, Regex? pattern);
}
