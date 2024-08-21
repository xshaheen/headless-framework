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
using Framework.BuildingBlocks;
using Framework.BuildingBlocks.Abstractions;
using Framework.BuildingBlocks.Helpers.IO;
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

    #region List

    public async ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] container,
        string? searchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        if (pageSize <= 0)
        {
            return PagedFileListResult.Empty;
        }

        var (_, containerUrl) = _BuildBucketUrl(container);
        var containerClient = _GetContainerClient(containerUrl);

        var result = new PagedFileListResult(_ =>
            _GetFiles(containerClient, searchPattern, 1, pageSize, cancellationToken)
        );

        await result.NextPageAsync();

        return result;
    }

    private async Task<NextPageResult> _GetFiles(
        BlobContainerClient containerClient,
        string? searchPattern,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        var pagingLimit = pageSize;
        var skip = (page - 1) * pagingLimit;

        if (pagingLimit < int.MaxValue)
        {
            pagingLimit++;
        }

        var list = await _GetFileListAsync(containerClient, searchPattern, pagingLimit, skip, cancellationToken);
        var hasMore = false;

        if (list.Count == pagingLimit)
        {
            hasMore = true;
            list.RemoveAt(pagingLimit - 1);
        }

        return new NextPageResult
        {
            Success = true,
            HasMore = hasMore,
            Files = list,
            NextPageFunc = hasMore
                ? _ => _GetFiles(containerClient, searchPattern, page + 1, pageSize, cancellationToken)
                : null,
        };
    }

    private async Task<List<BlobSpecification>> _GetFileListAsync(
        BlobContainerClient containerClient,
        string? searchPattern = null,
        int? limit = null,
        int? skip = null,
        CancellationToken cancellationToken = default
    )
    {
        if (limit.HasValue)
        {
            Argument.IsGreaterThan(limit.Value, 0);
        }

        var criteria = _GetRequestCriteria(searchPattern);

        var totalLimit =
            limit.GetValueOrDefault(int.MaxValue) < int.MaxValue
                ? skip.GetValueOrDefault() + limit!.Value
                : int.MaxValue;

        var blobs = new List<BlobSpecification>();

        var enumerable = containerClient
            .GetBlobsAsync(
                traits: BlobTraits.Metadata,
                states: BlobStates.None,
                prefix: criteria.Prefix,
                cancellationToken: cancellationToken
            )
            .AsPages(continuationToken: null, limit);

        await foreach (var item in enumerable)
        {
            // TODO: use continuation token to fetch more items

            foreach (var blobItem in item.Values)
            {
                // TODO: Verify if it's possible to create empty folders in storage. If so, don't return them.
                if (criteria.Pattern is not null && !criteria.Pattern.IsMatch(blobItem.Name))
                {
                    _logger.LogTrace("Skipping {Path}: Doesn't match pattern", blobItem.Name);

                    continue;
                }

                if (blobItem.Properties.ContentLength is > 0)
                {
                    var blobSpecification = new BlobSpecification
                    {
                        Path = blobItem.Name,
                        Size = blobItem.Properties.ContentLength.Value,
                        Created = blobItem.Properties.LastModified?.UtcDateTime ?? DateTime.MinValue,
                        Modified = blobItem.Properties.LastModified?.UtcDateTime ?? DateTime.MinValue,
                    };

                    blobs.Add(blobSpecification);
                }
            }

            if (blobs.Count >= totalLimit)
            {
                break;
            }

            if (item.ContinuationToken is null)
            {
                break;
            }
        }

        if (skip.HasValue)
        {
            blobs = blobs.Skip(skip.Value).ToList();
        }

        if (limit.HasValue)
        {
            blobs = blobs.Take(limit.Value).ToList();
        }

        return blobs;
    }

    private static SearchCriteria _GetRequestCriteria(string? searchPattern)
    {
        if (string.IsNullOrEmpty(searchPattern))
        {
            return new();
        }

        var normalizedSearchPattern = _NormalizePath(searchPattern);
        var wildcardPos = normalizedSearchPattern.IndexOf('*');
        var hasWildcard = wildcardPos >= 0;

        var prefix = normalizedSearchPattern;
        Regex? patternRegex = null;

        if (hasWildcard)
        {
            patternRegex = new Regex(
                $"^{Regex.Escape(normalizedSearchPattern).Replace("\\*", ".*?", StringComparison.Ordinal)}$"
            );

            var slashPos = normalizedSearchPattern.LastIndexOf('/');
            prefix = slashPos >= 0 ? normalizedSearchPattern[..slashPos] : string.Empty;
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

    [return: NotNullIfNotNull(nameof(path))]
    private static string? _NormalizePath(string? path)
    {
        return path?.Replace('\\', '/');
    }

    #endregion

    #region Dispose

    public void Dispose() { }

    #endregion
}
