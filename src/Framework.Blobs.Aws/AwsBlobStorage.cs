using System.Collections.Concurrent;
using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Flurl;
using Framework.Arguments;
using Framework.BuildingBlocks.Abstractions;
using Framework.BuildingBlocks.Constants;
using Framework.BuildingBlocks.Helpers;
using Microsoft.AspNetCore.StaticFiles;

namespace Framework.Blobs.Aws;

public sealed class AwsBlobStorage(IAmazonS3 s3, IContentTypeProvider contentTypeProvider, IClock clock) : IBlobStorage
{
    private static readonly ConcurrentDictionary<string, bool> _CreatedBuckets = new(StringComparer.Ordinal);
    private const string _DefaultCacheControl = "must-revalidate, max-age=7776000";

    public async ValueTask<IReadOnlyList<BlobUploadResult>> BulkUploadAsync(
        IReadOnlyCollection<BlobUploadRequest> blobs,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobs);
        Argument.IsNotNullOrEmpty(container);

        var tasks = blobs.Select(async file => await UploadAsync(file, container, cancellationToken));
        var result = await Task.WhenAll(tasks);

        return result;
    }

    public async ValueTask<BlobUploadResult> UploadAsync(
        BlobUploadRequest blob,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blob);

        var (trustedFileNameForDisplay, uniqueSaveName) = FileHelper.GetTrustedFileNames(blob.FileName);
        var (bucket, key) = _GetBucketAndKey(uniqueSaveName, container);

        await _CreateBucketIfNotExistsAsync(bucket, cancellationToken);

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = blob.Stream,
            ContentType = _GetContentType(blob.FileName),
            Headers = { CacheControl = _DefaultCacheControl, },
            Metadata =
            {
                ["x-amz-meta-upload-date"] = clock.Now.ToString("O"),
                ["x-amz-meta-original-name"] = trustedFileNameForDisplay,
                ["x-amz-meta-extension"] = Path.GetExtension(uniqueSaveName),
            },
        };

        _ = await s3.PutObjectAsync(request, cancellationToken);

        return new(uniqueSaveName, trustedFileNameForDisplay, blob.Stream.Length);
    }

    public async ValueTask<IReadOnlyList<bool>> BulkDeleteAsync(
        IReadOnlyCollection<string> blobNames,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobNames);
        Argument.IsNotNullOrEmpty(container);

        var (bucket, keyPrefix) = (container[0], Url.Combine(container.Skip(1).ToArray()));
        var keys = blobNames.Select(blobName => new KeyVersion { Key = Url.Combine(keyPrefix, blobName) }).ToList();

        var request = new DeleteObjectsRequest { BucketName = bucket, Objects = keys, };

        var response = await s3.DeleteObjectsAsync(request, cancellationToken);

        return keys.ConvertAll(key =>
        {
            return response.DeletedObjects.Exists(deletedObject =>
                string.Equals(deletedObject.Key, key.Key, StringComparison.Ordinal)
            );
        });
    }

    public async ValueTask<bool> DeleteAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        var (bucket, key) = _GetBucketAndKey(blobName, container);

        if (!await _ExistsAsync(bucket, key, cancellationToken))
        {
            return false;
        }

        var request = new DeleteObjectRequest { BucketName = bucket, Key = key, };

        await s3.DeleteObjectAsync(request, cancellationToken);

        return true;
    }

    public async ValueTask<BlobDownloadResult?> DownloadAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        var (bucket, key) = _GetBucketAndKey(blobName, container);

        if (!await _ExistsAsync(bucket, key, cancellationToken))
        {
            return null;
        }

        var request = new GetObjectRequest { BucketName = bucket, Key = key, };

        var response = await s3.GetObjectAsync(request, cancellationToken);

        if (response.HttpStatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        response.HttpStatusCode.EnsureSuccessStatusCode();

        return new(response.ResponseStream, blobName, _ToDictionary(response.Metadata));
    }

    public ValueTask<bool> ExistsAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        var (bucket, key) = _GetBucketAndKey(blobName, container);

        return _ExistsAsync(bucket, key, cancellationToken);
    }

    #region Helpers

    private async ValueTask<bool> _ExistsAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        // Make sure Blob Container exists.
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(s3, bucket))
        {
            return false;
        }

        try
        {
            await s3.GetObjectMetadataAsync(bucket, key, cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is AmazonS3Exception)
            {
#pragma warning disable ERP022
                return false;
#pragma warning restore ERP022
            }

            throw;
        }

        return true;
    }

    private async ValueTask _CreateBucketIfNotExistsAsync(
        string bucketName,
        CancellationToken cancellationToken = default
    )
    {
        if (_CreatedBuckets.ContainsKey(bucketName) || await AmazonS3Util.DoesS3BucketExistV2Async(s3, bucketName))
        {
            return;
        }

        await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucketName }, cancellationToken);
        _CreatedBuckets.TryAdd(bucketName, value: true);
    }

    private static (string Bucket, string Key) _GetBucketAndKey(string blobName, IReadOnlyList<string> container)
    {
        Argument.IsNotNullOrWhiteSpace(blobName);
        Argument.IsNotNullOrEmpty(container);

        var bucket = container[0];
        var key = Url.Combine(container.Skip(1).Append(blobName).ToArray());

        return (bucket, key);
    }

    private static Dictionary<string, object?>? _ToDictionary(MetadataCollection responseMetadata)
    {
        if (responseMetadata.Count == 0)
        {
            return null;
        }

        var dictionary = new Dictionary<string, object?>(responseMetadata.Count);

        foreach (var awsMetadataKey in responseMetadata.Keys)
        {
            var key = awsMetadataKey.StartsWith("x-amz-meta-", StringComparison.Ordinal)
                ? awsMetadataKey[11..]
                : awsMetadataKey;

            dictionary[key] = responseMetadata[awsMetadataKey];
        }

        return dictionary;
    }

    private string _GetContentType(string fileName)
    {
        return contentTypeProvider.TryGetContentType(fileName, out var contentType)
            ? contentType
            : ContentTypes.Application.OctetStream;
    }

    #endregion
}
