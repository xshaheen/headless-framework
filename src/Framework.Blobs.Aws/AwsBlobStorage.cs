// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Framework.Abstractions;
using Framework.Blobs.Internals;
using Framework.Checks;
using Framework.IO;
using Framework.Primitives;
using Framework.Urls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Framework.Blobs.Aws;

public sealed class AwsBlobStorage(
    IAmazonS3 s3,
    IMimeTypeProvider mimeTypeProvider,
    IClock clock,
    IOptions<AwsBlobStorageOptions> optionsAccessor,
    ILogger<AwsBlobStorage>? logger = null
) : IBlobStorage
{
    private const string _DefaultCacheControl = "must-revalidate, max-age=7776000";
    private const string _MetaDataHeaderPrefix = "x-amz-meta-";

    private readonly AwsBlobStorageOptions _options = optionsAccessor.Value;
    private readonly ILogger _logger = logger ?? NullLogger<AwsBlobStorage>.Instance;

    #region Create Container

    public async ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(container);

        await _CreateBucketAsync(_BuildBucketName(container), cancellationToken);
    }

    private async Task _CreateBucketAsync(string bucketName, CancellationToken cancellationToken)
    {
        if (await AmazonS3Util.DoesS3BucketExistV2Async(s3, bucketName).AnyContext())
        {
            return;
        }

        var request = new PutBucketRequest { BucketName = bucketName };
        await s3.PutBucketAsync(request, cancellationToken).AnyContext();
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

        await CreateContainerAsync(container, cancellationToken);
        var (bucket, objectKey) = _BuildObjectKey(blobName, container);

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
            await stream.CopyToAsync(streamCopy, cancellationToken).AnyContext();
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

        var response = await s3.PutObjectAsync(request, cancellationToken).AnyContext();

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

        var results = new Result<Exception>[blobs.Count];
        var index = 0;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxBulkParallelism,
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

    #endregion

    #region Delete

    public async ValueTask<bool> DeleteAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var (bucket, objectKey) = _BuildObjectKey(blobName, container);

        if (!await _ExistsAsync(bucket, objectKey, cancellationToken).AnyContext())
        {
            return false;
        }

        var request = new DeleteObjectRequest { BucketName = bucket, Key = objectKey };

        DeleteObjectResponse? response;

        try
        {
            response = await s3.DeleteObjectAsync(request, cancellationToken).AnyContext();
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

        var (bucket, keyPrefix) = (container[0], Url.Combine([.. container.Skip(1)]));

        var objectKeys = blobNames
            .Select(blobName => new KeyVersion { Key = Url.Combine(keyPrefix, blobName) })
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
            response = await s3.DeleteObjectsAsync(request, cancellationToken).AnyContext();
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            return objectKeys.ConvertAll(_ => Result<bool, Exception>.Ok(true));
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
                    results.Add(Result<bool, Exception>.Ok(true));
                }
            }

            return results;
        }

        response.HttpStatusCode.EnsureSuccessStatusCode();

        // No exceptions were thrown, so all items were deleted successfully.

        return objectKeys.ConvertAll(_ => Result<bool, Exception>.Ok(true));
    }

    public async ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    )
    {
        const int pageSize = 100;

        var (bucket, directories) = (container[0], container.Skip(1));
        var criteria = BlobStorageHelpers.GetRequestCriteria(directories, blobSearchPattern);

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
                listResponse = await s3.ListObjectsV2Async(listRequest, cancellationToken).AnyContext();
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

            _logger.LogInformation(
                "Deleting {FileCount} files matching {SearchPattern}",
                keys.Length,
                blobSearchPattern
            );

            var deleteResponse = await s3.DeleteObjectsAsync(deleteRequest, cancellationToken).AnyContext();

            if (deleteResponse.DeleteErrors?.Count > 0)
            {
                // retry 1 time, continue.
                var objects = deleteResponse.DeleteErrors.ConvertAll(e => new KeyVersion { Key = e.Key });
                var deleteRetryRequest = new DeleteObjectsRequest { BucketName = bucket, Objects = objects };

                var deleteRetryResponse = await s3.DeleteObjectsAsync(deleteRetryRequest, cancellationToken)
                    .AnyContext();

                if (deleteRetryResponse.DeleteErrors?.Count > 0)
                {
                    errors.AddRange(deleteRetryResponse.DeleteErrors);
                }
            }

            _logger.LogTrace(
                "Deleted {FileCount} files matching {SearchPattern}",
                deleteResponse.DeletedObjects?.Count ?? 0,
                blobSearchPattern
            );

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

        _logger.LogTrace("Finished deleting {FileCount} files matching {SearchPattern}", count, blobSearchPattern);

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

        // Ensure new bucket exists
        await _CreateBucketAsync(newBucket, cancellationToken);

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
            response = await s3.CopyObjectAsync(request, cancellationToken).AnyContext();
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
        if (!await CopyAsync(blobContainer, blobName, newBlobContainer, newBlobName, cancellationToken))
        {
            return false;
        }

        var (oldBucket, oldKey) = _BuildObjectKey(blobName, blobContainer);
        var (newBucket, newKey) = _BuildObjectKey(newBlobName, newBlobContainer);

        var deleteRequest = new DeleteObjectRequest { BucketName = oldBucket, Key = oldKey };
        var deleteResponse = await s3.DeleteObjectAsync(deleteRequest, cancellationToken).AnyContext();

        if (!deleteResponse.HttpStatusCode.IsSuccessStatusCode())
        {
            _logger.LogError(
                "Failed to delete original object {OldBucket}/{OldKey} after copy, rolling back",
                oldBucket,
                oldKey
            );

            // Compensating transaction: delete the copy to restore original state
            var compensate = new DeleteObjectRequest { BucketName = newBucket, Key = newKey };
            await s3.DeleteObjectAsync(compensate, cancellationToken).AnyContext();

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
        // Make sure Blob Container exists.
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(s3, bucket).AnyContext())
        {
            return false;
        }

        GetObjectMetadataResponse? response;

        try
        {
            response = await s3.GetObjectMetadataAsync(bucket, key, cancellationToken).AnyContext();
        }
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

        if (!await _ExistsAsync(bucket, key, cancellationToken).AnyContext())
        {
            return null;
        }

        var request = new GetObjectRequest { BucketName = bucket, Key = key };

        var response = await s3.GetObjectAsync(request, cancellationToken).AnyContext();

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
            response = await s3.GetObjectMetadataAsync(request, cancellationToken).AnyContext();
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
                response = await s3.ListObjectsV2Async(req, cancellationToken).AnyContext();
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

        await result.NextPageAsync(cancellationToken).AnyContext();

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

        _logger.LogTrace("Getting file list matching {Prefix} and {Pattern}...", criteria.Prefix, criteria.Pattern);

        ListObjectsV2Response? response;

        try
        {
            response = await s3.ListObjectsV2Async(req, cancellationToken).AnyContext();
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

    private static (string Bucket, string ObjectKey) _BuildObjectKey(string blobName, string[] container)
    {
        Argument.IsNotNullOrWhiteSpace(blobName);
        Argument.IsNotNullOrEmpty(container);

        PathValidation.ValidateContainer(container);
        PathValidation.ValidatePathSegment(blobName);

        var bucket = container[0];
        var objectKey = Url.Combine([.. container.Skip(1).Append(blobName)]);

        return (bucket, objectKey);
    }

    private static string _BuildBucketName(string[] container)
    {
        PathValidation.ValidateContainer(container);
        return container[0];
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

    #region Dispose

    public void Dispose() { }

    #endregion
}
