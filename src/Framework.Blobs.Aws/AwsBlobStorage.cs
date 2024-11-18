// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Flurl;
using Framework.BuildingBlocks;
using Framework.BuildingBlocks.Abstractions;
using Framework.BuildingBlocks.Helpers.IO;
using Framework.Checks;
using Framework.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Framework.Blobs.Aws;

public sealed class AwsBlobStorage : IBlobStorage
{
    private static readonly ConcurrentDictionary<string, bool> _CreatedBuckets = new(StringComparer.Ordinal);
    private const string _DefaultCacheControl = "must-revalidate, max-age=7776000";
    private const string _MetaDataHeaderPrefix = "x-amz-meta-";
    private const string _UploadDateMetadataKey = "upload-date";
    private const string _ExtensionMetadataKey = "extension";

    private readonly IAmazonS3 _s3;
    private readonly IMimeTypeProvider _mimeTypeProvider;
    private readonly IClock _clock;
    private readonly AwsBlobStorageOptions _options;
    private readonly ILogger _logger;

    public AwsBlobStorage(
        IAmazonS3 s3,
        IMimeTypeProvider mimeTypeProvider,
        IClock clock,
        IOptions<AwsBlobStorageOptions> optionsAccessor
    )
    {
        _s3 = s3;
        _mimeTypeProvider = mimeTypeProvider;
        _clock = clock;
        _options = optionsAccessor.Value;

        _logger =
            _options.LoggerFactory?.CreateLogger<AwsBlobStorageOptions>() ?? NullLogger<AwsBlobStorageOptions>.Instance;
    }

    #region Create Container

    public async ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(container);

        var bucketName = _BuildBucketName(container);

        if (_CreatedBuckets.ContainsKey(bucketName) || await AmazonS3Util.DoesS3BucketExistV2Async(_s3, bucketName))
        {
            return;
        }

        await _s3.PutBucketAsync(new PutBucketRequest { BucketName = bucketName }, cancellationToken);
        _CreatedBuckets.TryAdd(bucketName, value: true);
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

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = objectKey,
            InputStream = stream.CanSeek ? stream : AmazonS3Util.MakeStreamSeekable(stream),
            AutoCloseStream = !stream.CanSeek,
            AutoResetStreamPosition = false,
            ContentType = _mimeTypeProvider.GetMimeType(blobName),
            UseChunkEncoding = _options.UseChunkEncoding,
            CannedACL = _options.CannedAcl,
            Headers = { CacheControl = _DefaultCacheControl },
        };

        if (metadata is not null)
        {
            foreach (var m in metadata)
            {
                // Note: MetadataCollection automatically prefixed keys with "x-amz-meta-"
                request.Metadata[m.Key] = m.Value;
            }
        }

        request.Metadata[_UploadDateMetadataKey] = _clock.UtcNow.ToString("O");
        request.Metadata[_ExtensionMetadataKey] = Path.GetExtension(blobName);

        var response = await _s3.PutObjectAsync(request, cancellationToken).AnyContext();

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

        var tasks = blobs.Select(async blob =>
        {
            try
            {
                await UploadAsync(container, blob.FileName, blob.Stream, blob.Metadata, cancellationToken);

                return Result<Exception>.Success();
            }
            catch (Exception e)
            {
                return Result<Exception>.Fail(e);
            }
        });

        return await Task.WhenAll(tasks).WithAggregatedExceptions();
    }

    #endregion

    #region Delete

    public async ValueTask<bool> DeleteAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var (bucket, key) = _BuildObjectKey(blobName, container);

        if (!await _ExistsAsync(bucket, key, cancellationToken))
        {
            return false;
        }

        var request = new DeleteObjectRequest { BucketName = bucket, Key = key };
        var response = await _s3.DeleteObjectAsync(request, cancellationToken);

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
        Argument.IsNotNullOrEmpty(blobNames);
        Argument.IsNotNullOrEmpty(container);

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
            response = await _s3.DeleteObjectsAsync(request, cancellationToken);
        }
        catch (DeleteObjectsException e) // This exception is thrown when some items fail to delete.
        {
            var results = new List<Result<bool, Exception>>(blobNames.Count);

            foreach (var objectKey in objectKeys)
            {
                var deleteError = e.Response.DeleteErrors.Find(x =>
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
                    results.Add(Result<bool, Exception>.Success(operand: true));
                }
            }

            return results;
        }

        response.HttpStatusCode.EnsureSuccessStatusCode();

        // No exceptions were thrown, so all items were deleted successfully.

        return objectKeys.ConvertAll(_ => Result<bool, Exception>.Success(operand: true));
    }

    public async ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    )
    {
        const int pageSize = 100;

        var criteria = _GetRequestCriteria(blobSearchPattern);
        var (bucket, keyPrefix) = (container[0], Url.Combine([.. container.Skip(1).Append(criteria.Prefix)]));

        var listRequest = new ListObjectsV2Request
        {
            BucketName = bucket,
            Prefix = keyPrefix,
            MaxKeys = pageSize,
        };

        var deleteRequest = new DeleteObjectsRequest { BucketName = bucket };

        var errors = new List<DeleteError>();
        var count = 0;

        ListObjectsV2Response listResponse;

        do
        {
            listResponse = await _s3.ListObjectsV2Async(listRequest, cancellationToken).AnyContext();
            listRequest.ContinuationToken = listResponse.NextContinuationToken;

            var keys = _MatchesPattern(listResponse.S3Objects, criteria.Pattern)
                .Select(o => new KeyVersion { Key = o.Key })
                .ToArray();

            if (keys.Length == 0)
            {
                continue;
            }

            deleteRequest.Objects.AddRange(keys);

            _logger.LogInformation(
                "Deleting {FileCount} files matching {SearchPattern}",
                keys.Length,
                blobSearchPattern
            );
            var deleteResponse = await _s3.DeleteObjectsAsync(deleteRequest, cancellationToken).AnyContext();

            if (deleteResponse.DeleteErrors.Count > 0)
            {
                // retry 1 time, continue.
                var deleteRetryRequest = new DeleteObjectsRequest { BucketName = bucket };

                deleteRetryRequest.Objects.AddRange(
                    deleteResponse.DeleteErrors.Select(e => new KeyVersion { Key = e.Key })
                );

                var deleteRetryResponse = await _s3.DeleteObjectsAsync(deleteRetryRequest, cancellationToken)
                    .AnyContext();

                if (deleteRetryResponse.DeleteErrors.Count > 0)
                {
                    errors.AddRange(deleteRetryResponse.DeleteErrors);
                }
            }

            _logger.LogTrace(
                "Deleted {FileCount} files matching {SearchPattern}",
                deleteResponse.DeletedObjects.Count,
                blobSearchPattern
            );

            count += deleteResponse.DeletedObjects.Count;
            deleteRequest.Objects.Clear();
        } while (listResponse.IsTruncated && !cancellationToken.IsCancellationRequested);

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

        var request = new CopyObjectRequest
        {
            CannedACL = _options.CannedAcl,
            SourceBucket = oldBucket,
            SourceKey = oldKey,
            DestinationBucket = newBucket,
            DestinationKey = newKey,
        };

        var response = await _s3.CopyObjectAsync(request, cancellationToken).AnyContext();

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
        Argument.IsNotNullOrEmpty(blobName);
        Argument.IsNotNullOrEmpty(blobContainer);
        Argument.IsNotNullOrEmpty(newBlobName);
        Argument.IsNotNullOrEmpty(newBlobContainer);

        var (oldBucket, oldKey) = _BuildObjectKey(blobName, blobContainer);
        var (newBucket, newKey) = _BuildObjectKey(newBlobName, newBlobContainer);

        var request = new CopyObjectRequest
        {
            CannedACL = _options.CannedAcl,
            SourceBucket = oldBucket,
            SourceKey = oldKey,
            DestinationBucket = newBucket,
            DestinationKey = newKey,
        };

        var response = await _s3.CopyObjectAsync(request, cancellationToken).AnyContext();

        if (!response.HttpStatusCode.IsSuccessStatusCode())
        {
            _logger.LogError(
                "Failed to copy object from {OldBucket}/{OldKey} to {NewBucket}/{NewKey}",
                oldBucket,
                oldKey,
                newBucket,
                newKey
            );

            return false;
        }

        var deleteRequest = new DeleteObjectRequest { BucketName = oldBucket, Key = oldKey };

        var deleteResponse = await _s3.DeleteObjectAsync(deleteRequest, cancellationToken).AnyContext();

        return deleteResponse.HttpStatusCode.IsSuccessStatusCode();
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
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(_s3, bucket))
        {
            return false;
        }

        GetObjectMetadataResponse? response;

        try
        {
            response = await _s3.GetObjectMetadataAsync(bucket, key, cancellationToken).AnyContext();
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

    public async ValueTask<BlobDownloadResult?> DownloadAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var (bucket, key) = _BuildObjectKey(blobName, container);

        if (!await _ExistsAsync(bucket, key, cancellationToken))
        {
            return null;
        }

        var request = new GetObjectRequest { BucketName = bucket, Key = key };

        var response = await _s3.GetObjectAsync(request, cancellationToken);

        if (response.HttpStatusCode is HttpStatusCode.NotFound)
        {
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

        var stream = new ActionableStream(response.ResponseStream, () => response.Dispose());

        return new(stream, blobName, _ToDictionary(response.Metadata));
    }

    public async ValueTask<BlobInfo?> GetBlobInfoAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var (bucket, key) = _BuildObjectKey(blobName, container);

        var request = new GetObjectMetadataRequest { BucketName = bucket, Key = key };

        var response = await _s3.GetObjectMetadataAsync(request, cancellationToken);

        if (response.HttpStatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        response.HttpStatusCode.EnsureSuccessStatusCode();

        var modified = new DateTimeOffset(response.LastModified);
        var created = _GetUploadedDate(response.Metadata, modified);

        return new BlobInfo
        {
            Path = key,
            Created = created,
            Modified = modified,
            Size = response.ContentLength,
        };
    }

    #endregion

    #region Page

    public async ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] containers,
        string? blobSearchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(containers);
        Argument.IsPositive(pageSize);

        var bucket = _BuildBucketName(containers);
        var pattern =
            string.Join('/', containers.Skip(1)) + "/" + blobSearchPattern?.Replace('\\', '/').RemovePrefix('/');
        var criteria = _GetRequestCriteria(pattern);

        var result = new PagedFileListResult(_ =>
            _GetFiles(bucket, criteria, pageSize, continuationToken: null, cancellationToken)
        );

        await result.NextPageAsync().AnyContext();

        return result;
    }

    private async ValueTask<INextPageResult> _GetFiles(
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

        var response = await _s3.ListObjectsV2Async(req, cancellationToken).AnyContext();

        return new NextPageResult
        {
            Success = response.HttpStatusCode.IsSuccessStatusCode(),
            HasMore = response.IsTruncated,
            Blobs = _MatchesPattern(response.S3Objects, criteria.Pattern)
                .Select(_ToBlobInfo)
                .Where(spec => !_IsDirectory(spec))
                .ToList(),
            NextPageFunc = response.IsTruncated
                ? _ => _GetFiles(bucket, criteria, pageSize, response.NextContinuationToken, cancellationToken)
                : null,
        };
    }

    private static BlobInfo _ToBlobInfo(S3Object blob)
    {
        var modified = new DateTimeOffset(blob.LastModified);

        return new()
        {
            Path = blob.Key,
            Created = modified,
            Modified = modified,
            Size = blob.Size,
        };
    }

    private static bool _IsDirectory(BlobInfo file)
    {
        return file.Size is 0 && file.Path.EndsWith('/');
    }

    private static IEnumerable<S3Object> _MatchesPattern(IEnumerable<S3Object?> blobs, Regex? pattern)
    {
        return blobs.Where(blob =>
        {
            var path = blob?.Key;

            return path is not null && pattern?.IsMatch(path) != false;
        })!;
    }

    private static SearchCriteria _GetRequestCriteria(string? searchPattern)
    {
        if (string.IsNullOrEmpty(searchPattern))
        {
            return new();
        }

        var hasWildcard = searchPattern.Contains('*', StringComparison.Ordinal);

        var prefix = searchPattern;
        Regex? patternRegex = null;

        if (hasWildcard)
        {
            var searchRegexText = Regex.Escape(searchPattern).Replace("\\*", ".*?", StringComparison.Ordinal);
            patternRegex = new Regex($"^{searchRegexText}$", RegexOptions.ExplicitCapture, RegexPatterns.MatchTimeout);

            var slashPos = searchPattern.LastIndexOf('/');
            prefix = slashPos >= 0 ? searchPattern[..slashPos] : string.Empty;
        }

        return new SearchCriteria { Prefix = prefix, Pattern = patternRegex };
    }

    private sealed record SearchCriteria(string Prefix = "", Regex? Pattern = null);

    #endregion

    #region Build Urls

    private static (string Bucket, string ObjectKey) _BuildObjectKey(string blobName, IReadOnlyList<string> container)
    {
        Argument.IsNotNullOrWhiteSpace(blobName);
        Argument.IsNotNullOrEmpty(container);

        var bucket = _BuildBucketName(container);
        var key = Url.Combine([.. container.Skip(1).Append(blobName)]);

        return (bucket, key);
    }

    private static string _BuildBucketName(IReadOnlyList<string> container)
    {
        return container[0];
    }

    #endregion

    #region Metadata Converters

    private static Dictionary<string, string?>? _ToDictionary(MetadataCollection metadata)
    {
        if (metadata.Count == 0)
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

    private static DateTimeOffset _GetUploadedDate(MetadataCollection metadata, DateTimeOffset defaultValue)
    {
        var createdValue = metadata[_UploadDateMetadataKey];

        if (createdValue is null)
        {
            return defaultValue;
        }

        return DateTimeOffset.TryParseExact(
            createdValue,
            "o",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var value
        )
            ? value
            : defaultValue;
    }

    #endregion

    #region Dispose

    public void Dispose() { }

    #endregion
}
