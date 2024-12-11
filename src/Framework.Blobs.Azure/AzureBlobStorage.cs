// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
using Framework.Blobs.Azure.Internals;
using Framework.BuildingBlocks;
using Framework.BuildingBlocks.Abstractions;
using Framework.Checks;
using Framework.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Framework.Blobs.Azure;

public sealed class AzureBlobStorage : IBlobStorage
{
    private static readonly ConcurrentDictionary<string, bool> _CreatedContainers = new(StringComparer.Ordinal);
    private const string _DefaultCacheControl = "max-age=7776000, must-revalidate";
    private const string _UploadDateMetadataKey = "uploadDate";
    private const string _ExtensionMetadataKey = "extension";

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
        IOptionsSnapshot<AzureStorageOptions> configOptions
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

        _accountUrl = settings.AccountUrl;
        _keyCredential = new(settings.AccountName, settings.AccountKey);
        _serviceClient = new(new Uri(_accountUrl, UriKind.Absolute), _keyCredential, _blobClientOptions);
    }

    #region Create Container

    public async ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(container);

        var containerUrl = _BuildContainerUrl(container).ContainerUrl;

        if (_CreatedContainers.ContainsKey(containerUrl))
        {
            return;
        }

        var containerClient = _GetContainerClient(containerUrl);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);

        _CreatedContainers.TryAdd(containerUrl, value: true);
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

        var blobUrl = _BuildBlobUrl(blobName, container);
        var blobClient = _GetBlobClient(blobUrl);

        var httpHeader = new BlobHttpHeaders
        {
            ContentType = _mimeTypeProvider.GetMimeType(blobName),
            CacheControl = _DefaultCacheControl,
        };

        metadata ??= new Dictionary<string, string?>(StringComparer.Ordinal);
        metadata[_UploadDateMetadataKey] = _clock.UtcNow.ToString("O");
        metadata[_ExtensionMetadataKey] = Path.GetExtension(blobName);

        await blobClient.UploadAsync(stream, httpHeader, metadata, cancellationToken: cancellationToken);
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
        string[] container,
        IReadOnlyCollection<string> blobNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);

        if (blobNames.Count == 0)
        {
            return Array.Empty<Result<bool, Exception>>();
        }

        var batch = _serviceClient.GetBlobBatchClient();
        var blobUrls = blobNames.Select(blobName => new Uri(_BuildBlobUrl(blobName, container), UriKind.Absolute));
        var results = await batch.DeleteBlobsAsync(blobUrls, DeleteSnapshotsOption.IncludeSnapshots, cancellationToken);

        return results.ConvertAll(result => Result<bool, Exception>.Success(!result.IsError));
    }

    public async ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    )
    {
        var files = await GetPagedListAsync(container, blobSearchPattern, 500, cancellationToken);
        var count = 0;

        do
        {
            var names = files.Blobs.Select(file => file.BlobKey).ToArray();
            var results = await BulkDeleteAsync(container, names, cancellationToken);
            count += results.Count(x => x.Succeeded);
            await files.NextPageAsync().AnyContext();
        } while (files.HasMore);

        _logger.LogTrace(
            "Finished deleting {FileCount} files matching {@Container} {SearchPattern}",
            count,
            container,
            blobSearchPattern
        );

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

        var oldBlobUrl = _BuildBlobUrl(blobName, blobContainer);
        var newBlobUrl = _BuildBlobUrl(newBlobName, newBlobContainer);

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

        var copyResult = await CopyAsync(blobContainer, blobName, newBlobContainer, newBlobName, cancellationToken);

        if (!copyResult)
        {
            _logger.LogWarning("Unable to copy {BlobName} to {NewBlobName}", blobName, newBlobName);

            return false;
        }

        var deleteResult = await DeleteAsync(blobContainer, blobName, cancellationToken);

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
        string[] container,
        string blobName,
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
        string[] container,
        string blobName,
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

    public async ValueTask<BlobInfo?> GetBlobInfoAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        var blobUrl = _BuildBlobUrl(blobName, [.. container, "any"]);
        var blobClient = _GetBlobClient(blobUrl);

        Response<BlobProperties>? blobProperties;

        try
        {
            blobProperties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }

        if (!blobProperties.HasValue)
        {
            return null;
        }

        return new BlobInfo
        {
            BlobKey = blobName,
            Size = blobProperties.Value.ContentLength,
            Created = blobProperties.Value.CreatedOn,
            Modified = blobProperties.Value.LastModified,
        };
    }

    #endregion

    #region Page

    public async ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] container,
        string? blobSearchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);
        Argument.IsPositive(pageSize);

        var containerUrl = _BuildContainerUrl(container).ContainerUrl;
        var client = _GetContainerClient(containerUrl);
        var criteria = _GetRequestCriteria(container.Skip(1), blobSearchPattern);

        var result = new PagedFileListResult(async _ =>
            await _GetFilesAsync(client, criteria, pageSize, previousNextPageResult: null, cancellationToken)
        );

        await result.NextPageAsync().AnyContext();

        return result;
    }

    private async Task<AzureNextPageResult> _GetFilesAsync(
        BlobContainerClient client,
        SearchCriteria criteria,
        int pageSize,
        AzureNextPageResult? previousNextPageResult = null,
        CancellationToken cancellationToken = default
    )
    {
        var blobs = new List<BlobInfo>(previousNextPageResult?.ExtraLoadedBlobs ?? []);

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
                        _GetFilesAsync(client, criteria, pageSize, currentResult, cancellationToken),
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

        await foreach (var page in pages.WithCancellation(cancellationToken))
        {
            continuationToken = page.ContinuationToken;

            foreach (var blobItem in page.Values)
            {
                // Check if the blob name matches the pattern.
                if (criteria.Pattern?.IsMatch(blobItem.Name) == false)
                {
                    _logger.LogTrace("Skipping {Path}: Doesn't match pattern", blobItem.Name);

                    continue;
                }

                // Skip empty blobs.
                if (blobItem.Properties.ContentLength is not > 0)
                {
                    continue;
                }

                var blobSpecification = new BlobInfo
                {
                    BlobKey = blobItem.Name,
                    Size = blobItem.Properties.ContentLength.Value,
                    Created = blobItem.Properties.CreatedOn ?? DateTimeOffset.MinValue,
                    Modified = blobItem.Properties.LastModified ?? DateTimeOffset.MinValue,
                };

                blobs.Add(blobSpecification);
            }

            // If the continuation token is null or the blob count is greater than or equal to the page size hint, then break.
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
            ExtraLoadedBlobs = hasExtraLoadedBlobs ? blobs.Skip(pageSize).ToList() : Array.Empty<BlobInfo>(),
            ContinuationToken = continuationToken,
            AzureNextPageFunc = hasExtraLoadedBlobs
                ? currentResult => _GetFilesAsync(client, criteria, pageSize, currentResult, cancellationToken)
                : null,
        };
    }

    private static SearchCriteria _GetRequestCriteria(IEnumerable<string> directories, string? searchPattern)
    {
        searchPattern = Url.Combine(string.Join('/', directories), _NormalizePath(searchPattern));

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
            prefix = slashPos >= 0 ? searchPattern[..(slashPos + 1)] : string.Empty;
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

    private string _BuildBlobUrl(string blobName, IReadOnlyList<string> containers)
    {
        return Url.Combine([_accountUrl, .. containers, blobName]);
    }

    private (string Container, string ContainerUrl) _BuildContainerUrl(IReadOnlyList<string> containers)
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
