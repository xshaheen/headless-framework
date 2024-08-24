using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Flurl;
using Framework.Arguments;
using Framework.Blobs.Azure.Internals;
using Framework.BuildingBlocks;
using Framework.BuildingBlocks.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Framework.Blobs.Azure;

public sealed class AzureBlobStorage : IBlobStorage
{
    private static readonly ConcurrentDictionary<string, bool> _CreatedContainers = new(StringComparer.Ordinal);
    private const string _DefaultCacheControl = "max-age=7776000, must-revalidate";

    private readonly string _accountUrl;
    private readonly SpecializedBlobClientOptions _blobClientOptions;
    private readonly StorageSharedKeyCredential _keyCredential;
    private readonly BlobServiceClient _serviceClient;

    private readonly IMimeTypeProvider _mimeTypeProvider;
    private readonly IClock _clock;
    private readonly ILogger _logger;

    public AzureBlobStorage(
        IMimeTypeProvider mimeTypeProvider,
        IClock clock,
        IOptionsSnapshot<AzureStorageSettings> configOptions
    )
    {
        var settings = configOptions.Value;

        _mimeTypeProvider = mimeTypeProvider;
        _clock = clock;
        _logger = settings.LoggerFactory?.CreateLogger<AzureBlobStorage>() ?? NullLogger<AzureBlobStorage>.Instance;

        _blobClientOptions = new()
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

        _accountUrl = $"https://{settings.AccountName}.blob.core.windows.net";
        _keyCredential = new(settings.AccountName, settings.AccountKey);
        _serviceClient = new(new Uri(_accountUrl, UriKind.Absolute), _keyCredential, _blobClientOptions);
    }

    #region Create Container

    public async ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(container);

        var (_, bucketUrl) = _BuildBucketUrl(container);

        if (_CreatedContainers.ContainsKey(bucketUrl))
        {
            return;
        }

        var containerClient = _GetContainerClient(bucketUrl);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);

        _CreatedContainers.TryAdd(bucketUrl, value: true);
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
        Argument.IsNotNull(container);

        await CreateContainerAsync(container, cancellationToken);

        var blobUrl = _BuildBlobUrl(blob.FileName, container);
        var blobClient = _GetBlobClient(blobUrl);

        var httpHeader = new BlobHttpHeaders
        {
            ContentType = _mimeTypeProvider.GetMimeType(blob.FileName),
            CacheControl = _DefaultCacheControl,
        };

        var metadata = blob.Metadata ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        metadata["upload-date"] = _clock.Now.ToString("O");
        metadata["extension"] = Path.GetExtension(blob.FileName);

        await blobClient.UploadAsync(blob.Stream, httpHeader, metadata, cancellationToken: cancellationToken);
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
        var blobUrl = _BuildBlobUrl(blobName, container);
        var blobClient = _GetBlobClient(blobUrl);

        var response = await blobClient.DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: cancellationToken
        );

        return response.Value;
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

        var batch = _serviceClient.GetBlobBatchClient();
        var blobUrls = blobNames.Select(blobName => new Uri(_BuildBlobUrl(blobName, container), UriKind.Absolute));
        var results = await batch.DeleteBlobsAsync(blobUrls, DeleteSnapshotsOption.IncludeSnapshots, cancellationToken);

        return results.ConvertAll(result => Result<bool, Exception>.Success(!result.IsError));
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

        var (oldBucket, _, oldBlobUrl) = _BuildUrls(blobName, blobContainer);
        var (newBucket, _, newBlobUrl) = _BuildUrls(newBlobName, newBlobContainer);

        if (oldBucket == newBucket)
        {
            throw new InvalidOperationException("Cannot copy file to the same bucket.");
        }

        var newBlobClient = _GetBlobClient(newBlobUrl);

        var copyResult = await newBlobClient.StartCopyFromUriAsync(
            new Uri(oldBlobUrl, UriKind.Absolute),
            cancellationToken: cancellationToken
        );

        await copyResult.WaitForCompletionAsync(cancellationToken);

        return copyResult.HasCompleted;
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

        var copyResult = await CopyAsync(blobName, blobContainer, newBlobName, newBlobContainer, cancellationToken);

        if (!copyResult)
        {
            _logger.LogWarning("Unable to copy {BlobName} to {NewBlobName}", blobName, newBlobName);

            return false;
        }

        var deleteResult = await DeleteAsync(blobName, blobContainer, cancellationToken);

        if (!deleteResult)
        {
            _logger.LogWarning("Unable to delete {BlobName}", blobName);

            return false;
        }

        return true;
    }

    #endregion

    #region Exists

    public async ValueTask<bool> ExistsAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        var blobUrl = _BuildBlobUrl(blobName, container);
        var client = _GetBlobClient(blobUrl);

        var response = await client.ExistsAsync(cancellationToken);

        return response.Value;
    }

    #endregion

    #region Download

    public async ValueTask<BlobDownloadResult?> DownloadAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        var client = _GetBlobClient(_BuildBlobUrl(blobName, container));

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
        Argument.IsExclusiveBetween(pageSize, 1, 5000);

        var containerUrl = Url.Combine(_accountUrl, containers[0]);
        var client = _GetContainerClient(containerUrl);
        var pattern = string.Join("/", containers.Skip(1)) + "/" + searchPattern?.Replace('\\', '/').RemovePrefix('/');
        var criteria = _GetRequestCriteria(pattern);

        var result = new PagedFileListResult(async _ =>
            await _GetFiles(client, criteria, pageSize, null, cancellationToken).AnyContext()
        );

        await result.NextPageAsync().AnyContext();

        return result;
    }

    private async Task<AzureNextPageResult> _GetFiles(
        BlobContainerClient client,
        SearchCriteria criteria,
        int pageSize,
        AzureNextPageResult? previousNextPageResult = null,
        CancellationToken cancellationToken = default
    )
    {
        var blobs = new List<BlobSpecification>(previousNextPageResult?.ExtraLoadedBlobs ?? []);

        // If the previous result has more blobs than the page size, then return the result.
        if (previousNextPageResult is not null)
        {
            var remainingBlobsCount = pageSize - blobs.Count;

            if (remainingBlobsCount <= 0)
            {
                return new AzureNextPageResult
                {
                    Success = true,
                    HasMore = remainingBlobsCount != 0 || previousNextPageResult.ContinuationToken is not null,
                    Blobs = blobs.Take(pageSize).ToList(),
                    ExtraLoadedBlobs = blobs.Skip(pageSize).ToList(),
                    ContinuationToken = previousNextPageResult.ContinuationToken,
                    AzureNextPageFunc = currentResult =>
                        _GetFiles(client, criteria, pageSize, currentResult, cancellationToken),
                };
            }
        }

        var pageSizeToLoad = pageSize - blobs.Count;
        var continuationToken = previousNextPageResult?.ContinuationToken;

        var pages = client
            .GetBlobsAsync(
                traits: BlobTraits.Metadata,
                states: BlobStates.None,
                prefix: criteria.Prefix,
                cancellationToken: cancellationToken
            )
            .AsPages(continuationToken, pageSizeToLoad);

        // AsPages parameter pageSizeHint - It's not guaranteed that the value will be respected.
        // Note that if the listing operation crosses a partition boundary, then the service
        // will return a continuation token for retrieving the remainder of the results.
        // For this reason, it is possible that the service will return fewer results than the specified.

        await foreach (var page in pages)
        {
            continuationToken = page.ContinuationToken;

            foreach (var blobItem in page.Values)
            {
                // Check if the blob name matches the pattern.
                if (criteria.Pattern is not null && !criteria.Pattern.IsMatch(blobItem.Name))
                {
                    _logger.LogTrace("Skipping {Path}: Doesn't match pattern", blobItem.Name);

                    continue;
                }

                // Skip empty blobs.
                if (blobItem.Properties.ContentLength is not > 0)
                {
                    continue;
                }

                var blobSpecification = new BlobSpecification
                {
                    Path = blobItem.Name,
                    Size = blobItem.Properties.ContentLength.Value,
                    Created = blobItem.Properties.LastModified?.UtcDateTime ?? DateTime.MinValue,
                    Modified = blobItem.Properties.LastModified?.UtcDateTime ?? DateTime.MinValue,
                };

                blobs.Add(blobSpecification);
            }

            // If the continuation token is null or the blobs count is greater than or equal to the page size hint, then break.
            if (page.ContinuationToken is null || blobs.Count >= pageSizeToLoad)
            {
                break;
            }
        }

        var hasExtraLoadedBlobs = blobs.Count > pageSize;

        return new AzureNextPageResult
        {
            Success = true,
            HasMore = hasExtraLoadedBlobs,
            Blobs = blobs.Take(pageSize).ToList(),
            ExtraLoadedBlobs = hasExtraLoadedBlobs ? blobs.Skip(pageSize).ToList() : Array.Empty<BlobSpecification>(),
            ContinuationToken = continuationToken,
            AzureNextPageFunc = hasExtraLoadedBlobs
                ? currentResult => _GetFiles(client, criteria, pageSize, currentResult, cancellationToken)
                : null,
        };
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
            patternRegex = new Regex(
                $"^{Regex.Escape(searchPattern).Replace("\\*", ".*?", StringComparison.Ordinal)}$"
            );

            var slashPos = searchPattern.LastIndexOf('/');
            prefix = slashPos >= 0 ? searchPattern[..slashPos] : string.Empty;
        }

        return new(prefix, patternRegex);
    }

    private sealed record SearchCriteria(string Prefix = "", Regex? Pattern = null);

    #endregion

    #region Build Clients

    private BlobClient _GetBlobClient(string blobUrl)
    {
        return new(new Uri(blobUrl, UriKind.Absolute), _keyCredential, _blobClientOptions);
    }

    private BlobContainerClient _GetContainerClient(string containerUrl)
    {
        return new(new Uri(containerUrl, UriKind.Absolute), _keyCredential, _blobClientOptions);
    }

    #endregion

    #region Build URLs

    private (string Bucket, string BucketUrl, string BlobUrl) _BuildUrls(
        string blobName,
        IReadOnlyList<string> containers
    )
    {
        Argument.IsNotNullOrEmpty(containers);
        Argument.IsNotNullOrWhiteSpace(blobName);

        var (bucket, bucketUrl) = _BuildBucketUrl(containers);
        var blobUrl = _BuildBlobUrl(blobName, containers);

        return (bucket, bucketUrl, blobUrl);
    }

    private string _BuildBlobUrl(string blobName, IReadOnlyList<string> containers)
    {
        return Url.Combine([_accountUrl, .. containers, blobName]);
    }

    private (string Bucket, string BucketUrl) _BuildBucketUrl(IReadOnlyList<string> containers)
    {
        var bucket = containers[0];
        var bucketUrl = Url.Combine(_accountUrl, containers[0]);

        return (bucket, bucketUrl);
    }

    #endregion

    #region Dispose

    public void Dispose() { }

    #endregion
}
