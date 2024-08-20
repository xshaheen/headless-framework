using System.Collections.Concurrent;
using Azure;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Flurl;
using Framework.Arguments;
using Framework.BuildingBlocks.Abstractions;
using Framework.BuildingBlocks.Constants;
using Framework.BuildingBlocks.Helpers;
using Framework.BuildingBlocks.Helpers.IO;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace Framework.Blobs.Azure;

public sealed class AzureBlobStorage : IBlobStorage
{
    private static readonly ConcurrentDictionary<string, bool> _CreatedContainers = new(StringComparer.Ordinal);
    private const string _DefaultCacheControl = "max-age=7776000, must-revalidate";

    private readonly StorageSharedKeyCredential _keyCredential;
    private readonly SpecializedBlobClientOptions _blobClientOptions;
    private readonly IContentTypeProvider _contentTypeProvider;
    private readonly IClock _clock;
    private readonly string _baseUrl;

    public AzureBlobStorage(
        IContentTypeProvider contentTypeProvider,
        IClock clock,
        IOptionsSnapshot<AzureStorageOptions> configOptions
    )
    {
        _contentTypeProvider = contentTypeProvider;
        _clock = clock;
        var config = configOptions.Value;
        _baseUrl = $"https://{config.AccountName}.blob.core.windows.net";
        _keyCredential = new StorageSharedKeyCredential(config.AccountName, config.AccountKey);

        _blobClientOptions = new SpecializedBlobClientOptions
        {
            Retry =
            {
                MaxRetries = 3,
                Mode = RetryMode.Exponential,
                Delay = TimeSpan.FromSeconds(0.4),
                NetworkTimeout = TimeSpan.FromSeconds(10),
                MaxDelay = TimeSpan.FromMinutes(1),
            },
        };
    }

    public async ValueTask<IReadOnlyList<BlobUploadResult>> BulkUploadAsync(
        IReadOnlyCollection<BlobUploadRequest> blobs,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobs);
        Argument.IsNotNullOrEmpty(container);

        var tasks = blobs.Select(async blob => await UploadAsync(blob, container, cancellationToken));
        var result = await Task.WhenAll(tasks);

        return result;
    }

    public async ValueTask<IReadOnlyList<bool>> BulkDeleteAsync(
        IReadOnlyCollection<string> blobNames,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobNames);
        Argument.IsNotNullOrEmpty(container);

        var tasks = blobNames.Select(async fileName => await DeleteAsync(fileName, container, cancellationToken));
        var result = await Task.WhenAll(tasks);

        return result;
    }

    public ValueTask CreateContainerAsync(string[] container)
    {
        Argument.IsNotNullOrEmpty(container);

        return _CreateContainerIfNotExistsAsync(container[0]);
    }

    public async ValueTask<BlobUploadResult> UploadAsync(
        BlobUploadRequest blob,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blob);

        var (trustedFileNameForDisplay, uniqueSaveName) = FileHelper.GetTrustedFileNames(blob.FileName);
        var (bucket, blobUrl) = _GetBucketAndBlobUrl(uniqueSaveName, container);

        await _CreateContainerIfNotExistsAsync(bucket, cancellationToken: cancellationToken);
        var client = _GetBlobClient(blobUrl);

        var httpHeader = new BlobHttpHeaders
        {
            ContentType = _GetContentType(uniqueSaveName),
            CacheControl = _DefaultCacheControl,
        };

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["upload-date"] = _clock.Now.ToString("O"),
            ["original-name"] = trustedFileNameForDisplay,
            ["extension"] = Path.GetExtension(uniqueSaveName),
        };

        _ = await client.UploadAsync(blob.Stream, httpHeader, metadata, cancellationToken: cancellationToken);

        return new(uniqueSaveName, trustedFileNameForDisplay, blob.Stream.Length);
    }

    public async ValueTask<bool> ExistsAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        var client = _GetBlobClient(_CreateBlobUrl(blobName, container));

        var response = await client.ExistsAsync(cancellationToken);

        return response.Value;
    }

    public async ValueTask<bool> DeleteAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        var blobClient = _GetBlobClient(_CreateBlobUrl(blobName, container));

        var response = await blobClient.DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: cancellationToken
        );

        return response.Value;
    }

    public async ValueTask<BlobDownloadResult?> DownloadAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        var client = _GetBlobClient(_CreateBlobUrl(blobName, container));

        var memoryStream = new MemoryStream();

        try
        {
            await client.DownloadToAsync(memoryStream, cancellationToken);
        }
        catch (RequestFailedException e)
            when (e.ErrorCode == BlobErrorCode.BlobNotFound || e.ErrorCode == BlobErrorCode.ContainerNotFound)
        {
            return null;
        }

        return new(memoryStream, blobName);
    }

    #region Helpers

    private async ValueTask _CreateContainerIfNotExistsAsync(
        string container,
        PublicAccessType accessType = PublicAccessType.Blob,
        CancellationToken cancellationToken = default
    )
    {
        if (_CreatedContainers.ContainsKey(container))
        {
            return;
        }

        var containerUri = Url.Combine(_baseUrl, container);
        var containerClient = _GetContainerClient(containerUri);

        await containerClient.CreateIfNotExistsAsync(accessType, cancellationToken: cancellationToken);

        _CreatedContainers.TryAdd(container, value: true);
    }

    private (string Bucket, string BlobUrl) _GetBucketAndBlobUrl(string blobName, IReadOnlyList<string> container)
    {
        Argument.IsNotNullOrEmpty(container);
        Argument.IsNotNullOrWhiteSpace(blobName);

        var bucket = container[0];
        var blobUrl = _CreateBlobUrl(blobName, container);

        return (bucket, blobUrl);
    }

    private string _CreateBlobUrl(string blobName, IReadOnlyCollection<string> container)
    {
        Argument.IsNotNullOrEmpty(container);
        Argument.IsNotNullOrWhiteSpace(blobName);

        var blobUrl = Url.Combine(container.Prepend(_baseUrl).Append(blobName).ToArray());

        return blobUrl;
    }

    private string _GetContentType(string fileName)
    {
        return _contentTypeProvider.TryGetContentType(fileName, out var contentType)
            ? contentType
            : ContentTypes.Application.OctetStream;
    }

    private BlobClient _GetBlobClient(string blobUri)
    {
        var uri = new Uri(blobUri, UriKind.Absolute);

        return new(uri, _keyCredential, _blobClientOptions);
    }

    private BlobContainerClient _GetContainerClient(string containerUri)
    {
        var uri = new Uri(containerUri, UriKind.Absolute);

        return new(uri, _keyCredential, _blobClientOptions);
    }

    #endregion
}
