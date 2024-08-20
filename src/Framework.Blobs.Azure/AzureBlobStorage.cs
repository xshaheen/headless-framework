using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
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

    private readonly string _accountUrl;
    private readonly SpecializedBlobClientOptions _blobClientOptions;
    private readonly StorageSharedKeyCredential _keyCredential;
    private readonly BlobServiceClient _serviceClient;

    private readonly IContentTypeProvider _contentTypeProvider;
    private readonly IClock _clock;

    public AzureBlobStorage(
        IContentTypeProvider contentTypeProvider,
        IClock clock,
        IOptionsSnapshot<AzureStorageOptions> configOptions
    )
    {
        _contentTypeProvider = contentTypeProvider;
        _clock = clock;

        var config = configOptions.Value;
        _accountUrl = $"https://{config.AccountName}.blob.core.windows.net";

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

        _keyCredential = new(config.AccountName, config.AccountKey);
        _serviceClient = new(new Uri(_accountUrl, UriKind.Absolute), _keyCredential, _blobClientOptions);
    }

    public ValueTask CreateContainerAsync(string[] container)
    {
        Argument.IsNotNullOrEmpty(container);

        return _CreateContainerIfNotExistsAsync(container[0]);
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
        // TODO: Task.WhenAll has exception handling issues and should be replaced with a more robust
        //       solution like Polly and handling exceptions in a more controlled manner.
        var result = await Task.WhenAll(tasks).WithAggregatedExceptions();
        ;

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

        var batch = _serviceClient.GetBlobBatchClient();
        var blobUrls = blobNames.Select(blobName => new Uri(_BuildBlobUrl(blobName, container), UriKind.Absolute));
        var results = await batch.DeleteBlobsAsync(blobUrls, DeleteSnapshotsOption.IncludeSnapshots, cancellationToken);

        return results.ConvertAll(result => !result.IsError);
    }

    public async ValueTask<BlobUploadResult> UploadAsync(
        BlobUploadRequest blob,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blob);
        Argument.IsNotNull(container);

        var (trustedFileNameForDisplay, uniqueSaveName) = FileHelper.GetTrustedFileNames(blob.FileName);
        var (_, bucketUrl, blobUrl) = _BuildUrls(uniqueSaveName, container);

        await _CreateContainerIfNotExistsAsync(bucketUrl, cancellationToken: cancellationToken);

        var blobClient = _GetBlobClient(blobUrl);

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

        await blobClient.UploadAsync(blob.Stream, httpHeader, metadata, cancellationToken: cancellationToken);

        return new(uniqueSaveName, trustedFileNameForDisplay, blob.Stream.Length);
    }

    public async ValueTask<bool> RenameFileAsync(
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

        var copyResult = await CopyFileAsync(blobName, blobContainer, newBlobName, newBlobContainer, cancellationToken);

        if (copyResult is null)
        {
            return false;
        }
    }

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

    public async ValueTask<BlobUploadResult?> CopyFileAsync(
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

        return !copyResult.HasCompleted ? null : new(newBlobName, newBlobName, copyResult.Value);
    }

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
                : null
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

        var blobs = new List<BlobItem>();
        string? continuationToken = null;

        var enumerable = containerClient
            .GetBlobsAsync(
                traits: BlobTraits.Metadata,
                states: BlobStates.None,
                prefix: criteria.Prefix,
                cancellationToken: cancellationToken
            )
            .AsPages(continuationToken, limit);

        await foreach (var item in enumerable)
        {
            continuationToken = item.ContinuationToken;
            blobs.AddRange(item.Values);
        }

        do
        {
            var listingResult = await _container
                .ListBlobsSegmentedAsync(
                    criteria.Prefix,
                    true,
                    BlobListingDetails.Metadata,
                    limit,
                    continuationToken,
                    null,
                    null,
                    cancellationToken
                )
                .AnyContext();

            continuationToken = listingResult.ContinuationToken;

            foreach (var blob in listingResult.Results.OfType<CloudBlockBlob>())
            {
                // TODO: Verify if it's possible to create empty folders in storage. If so, don't return them.
                if (criteria.Pattern != null && !criteria.Pattern.IsMatch(blob.Name))
                {
                    _logger.LogTrace("Skipping {Path}: Doesn't match pattern", blob.Name);

                    continue;
                }

                blobs.Add(blob);
            }
        } while (continuationToken != null && blobs.Count < totalLimit);

        if (skip.HasValue)
        {
            blobs = blobs.Skip(skip.Value).ToList();
        }

        if (limit.HasValue)
        {
            blobs = blobs.Take(limit.Value).ToList();
        }

        return blobs.Select(blob => blob.ToFileInfo()).ToList();
    }

    #region Helpers

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

    private async ValueTask _CreateContainerIfNotExistsAsync(
        string containerUrl,
        PublicAccessType accessType = PublicAccessType.Blob,
        CancellationToken cancellationToken = default
    )
    {
        if (_CreatedContainers.ContainsKey(containerUrl))
        {
            return;
        }

        var containerClient = _GetContainerClient(containerUrl);
        await containerClient.CreateIfNotExistsAsync(accessType, cancellationToken: cancellationToken);

        _CreatedContainers.TryAdd(containerUrl, value: true);
    }

    private string _GetContentType(string fileName)
    {
        return _contentTypeProvider.TryGetContentType(fileName, out var contentType)
            ? contentType
            : ContentTypes.Application.OctetStream;
    }

    #endregion

    #region Clients

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

    #region Types

    private sealed record SearchCriteria(string Prefix = "", Regex? Pattern = null);

    #endregion
}
