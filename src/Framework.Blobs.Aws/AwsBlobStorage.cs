using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Flurl;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.BuildingBlocks.Helpers.IO;
using Framework.Kernel.Checks;
using Framework.Kernel.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Framework.Blobs.Aws;

public sealed class AwsBlobStorage : IBlobStorage
{
    private static readonly ConcurrentDictionary<string, bool> _CreatedBuckets = new(StringComparer.Ordinal);
    private const string _DefaultCacheControl = "must-revalidate, max-age=7776000";

    private readonly IAmazonS3 _s3;
    private readonly IMimeTypeProvider _mimeTypeProvider;
    private readonly IClock _clock;
    private readonly AwsBlobStorageSettings _settings;
    private readonly ILogger _logger;

    public AwsBlobStorage(
        IAmazonS3 s3,
        IMimeTypeProvider mimeTypeProvider,
        IClock clock,
        IOptions<AwsBlobStorageSettings> options
    )
    {
        _s3 = s3;
        _mimeTypeProvider = mimeTypeProvider;
        _clock = clock;
        _settings = options.Value;

        _logger =
            _settings.LoggerFactory?.CreateLogger<AwsBlobStorageSettings>()
            ?? NullLogger<AwsBlobStorageSettings>.Instance;
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
        BlobUploadRequest blob,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blob);

        await CreateContainerAsync(container, cancellationToken);
        var (bucket, objectKey) = _BuildObjectKey(blob.FileName, container);

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = objectKey,
            InputStream = blob.Stream.CanSeek ? blob.Stream : AmazonS3Util.MakeStreamSeekable(blob.Stream),
            AutoCloseStream = !blob.Stream.CanSeek,
            AutoResetStreamPosition = false,
            ContentType = _mimeTypeProvider.GetMimeType(blob.FileName),
            UseChunkEncoding = _settings.UseChunkEncoding,
            CannedACL = _settings.CannedAcl,
            Headers = { CacheControl = _DefaultCacheControl, },
        };

        if (blob.Metadata is not null)
        {
            foreach (var metadata in blob.Metadata)
            {
                // Note: MetadataCollection is automatically prefixed with "x-amz-meta-"
                request.Metadata[metadata.Key] = metadata.Value;
            }
        }

        request.Metadata["upload-date"] = _clock.Now.ToString("O");
        request.Metadata["extension"] = Path.GetExtension(blob.FileName);

        var response = await _s3.PutObjectAsync(request, cancellationToken).AnyContext();

        response.HttpStatusCode.EnsureSuccessStatusCode();
    }

    #endregion

    #region Bulk Upload

    public async ValueTask<IReadOnlyList<Result<Exception>>> BulkUploadAsync(
        IReadOnlyCollection<BlobUploadRequest> blobs,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobs);
        Argument.IsNotNullOrEmpty(container);

        var tasks = blobs.Select(async blob =>
        {
            try
            {
                await UploadAsync(blob, container, cancellationToken);

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
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        var (bucket, key) = _BuildObjectKey(blobName, container);

        if (!await _ExistsAsync(bucket, key, cancellationToken))
        {
            return false;
        }

        var request = new DeleteObjectRequest { BucketName = bucket, Key = key, };
        var response = await _s3.DeleteObjectAsync(request, cancellationToken);

        response.HttpStatusCode.EnsureSuccessStatusCode();

        return true;
    }

    #endregion

    #region Bulk Delete

    public async ValueTask<IReadOnlyList<Result<bool, Exception>>> BulkDeleteAsync(
        IReadOnlyCollection<string> blobNames,
        string[] container,
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
                var deleteError = e.Response.DeleteErrors.FirstOrDefault(x => x.Key == objectKey.Key);

                if (deleteError is not null)
                {
                    var exception = new InvalidOperationException(
                        $"Error deleting item with Code: {deleteError.Code} and Message: {deleteError.Message}"
                    );

                    results.Add(Result<bool, Exception>.Fail(exception));
                }
                else
                {
                    results.Add(Result<bool, Exception>.Success(true));
                }
            }

            return results;
        }

        response.HttpStatusCode.EnsureSuccessStatusCode();

        // No exceptions were thrown, so all items were deleted successfully.

        return objectKeys.ConvertAll(_ => Result<bool, Exception>.Success(true));
    }

    #endregion

    #region Copy

    public async ValueTask<bool> CopyAsync(
        string blobName,
        string[] blobContainer,
        string newBlobName,
        string[] newBlobContainer,
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
            CannedACL = _settings.CannedAcl,
            SourceBucket = oldBucket,
            SourceKey = oldKey,
            DestinationBucket = newBucket,
            DestinationKey = newBucket,
        };

        var response = await _s3.CopyObjectAsync(request, cancellationToken).AnyContext();

        return response.HttpStatusCode.IsSuccessStatusCode();
    }

    #endregion

    #region Rename

    public async ValueTask<bool> RenameAsync(
        string blobName,
        string[] blobContainer,
        string newBlobName,
        string[] newBlobContainer,
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
            CannedACL = _settings.CannedAcl,
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
        string blobName,
        string[] container,
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
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        var (bucket, key) = _BuildObjectKey(blobName, container);

        if (!await _ExistsAsync(bucket, key, cancellationToken))
        {
            return null;
        }

        var request = new GetObjectRequest { BucketName = bucket, Key = key, };

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

    #endregion

    #region Page

    public async ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] containers,
        string? searchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(containers);
        Argument.IsPositive(pageSize);

        var bucket = _BuildBucketName(containers);
        var pattern = string.Join("/", containers.Skip(1)) + "/" + searchPattern?.Replace('\\', '/').RemovePrefix('/');
        var criteria = _GetRequestCriteria(pattern);
        var result = new PagedFileListResult(_ => _GetFiles(bucket, criteria, pageSize, null, cancellationToken));
        await result.NextPageAsync().AnyContext();

        return result;
    }

    private async Task<INextPageResult> _GetFiles(
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
            ContinuationToken = continuationToken
        };

        _logger.LogTrace("Getting file list matching {Prefix} and {Pattern}...", criteria.Prefix, criteria.Pattern);

        var response = await _s3.ListObjectsV2Async(req, cancellationToken).AnyContext();

        return new NextPageResult
        {
            Success = response.HttpStatusCode.IsSuccessStatusCode(),
            HasMore = response.IsTruncated,
            Blobs = _MatchesPattern(response.S3Objects, criteria.Pattern)
                .Select(_ToBlobSpecification)
                .Where(spec => !_IsDirectory(spec))
                .ToList(),
            NextPageFunc = response.IsTruncated
                ? _ => _GetFiles(bucket, criteria, pageSize, response.NextContinuationToken, cancellationToken)
                : null
        };
    }

    private static BlobSpecification _ToBlobSpecification(S3Object blob)
    {
        return new()
        {
            Path = blob.Key,
            Created = blob.LastModified,
            Modified = blob.LastModified,
            Size = blob.Size,
        };
    }

    private static bool _IsDirectory(BlobSpecification file)
    {
        return file.Size is 0 && file.Path.EndsWith('/');
    }

    private static IEnumerable<S3Object> _MatchesPattern(IEnumerable<S3Object?> blobs, Regex? pattern)
    {
        return blobs.Where(blob =>
        {
            var path = blob?.Key;

            return path is not null && (pattern is null || pattern.IsMatch(path));
        })!;
    }

    private static SearchCriteria _GetRequestCriteria(string? searchPattern)
    {
        if (string.IsNullOrEmpty(searchPattern))
        {
            return new();
        }

        var wildcardPos = searchPattern.IndexOf('*', StringComparison.Ordinal);
        var hasWildcard = wildcardPos >= 0;

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

    private static Dictionary<string, object?>? _ToDictionary(MetadataCollection metadata)
    {
        if (metadata.Count == 0)
        {
            return null;
        }

        var dictionary = new Dictionary<string, object?>(metadata.Count, StringComparer.Ordinal);

        foreach (var awsMetadataKey in metadata.Keys)
        {
            var key = awsMetadataKey.StartsWith("x-amz-meta-", StringComparison.Ordinal)
                ? awsMetadataKey[11..]
                : awsMetadataKey;

            dictionary[key] = metadata[awsMetadataKey];
        }

        return dictionary;
    }

    #endregion

    #region Dispose

    public void Dispose() { }

    #endregion
}
