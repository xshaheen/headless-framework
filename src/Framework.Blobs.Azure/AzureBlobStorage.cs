// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Framework.Abstractions;
using Framework.Blobs.Azure.Internals;
using Framework.Checks;
using Framework.Constants;
using Framework.Primitives;
using Framework.Urls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Blobs.Azure;

public sealed class AzureBlobStorage(
    BlobServiceClient blobServiceClient,
    IMimeTypeProvider mimeTypeProvider,
    IClock clock,
    IOptions<AzureStorageOptions> optionAccessor,
    ILogger<AzureBlobStorage> logger
) : IBlobStorage
{
    private static readonly ConcurrentDictionary<string, bool> _CreatedContainers = new(StringComparer.Ordinal);
    private const string _UploadDateMetadataKey = "uploadDate";
    private const string _ExtensionMetadataKey = "extension";

    private readonly AzureStorageOptions _option = optionAccessor.Value;

    #region Create Container

    public async ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(container);

        var blobContainer = _GetContainer(container);

        if (_CreatedContainers.ContainsKey(blobContainer))
        {
            return;
        }

        var containerClient = blobServiceClient.GetBlobContainerClient(blobContainer);

        await containerClient
            .CreateIfNotExistsAsync(_option.ContainerPublicAccessType, cancellationToken: cancellationToken)
            .AnyContext();

        _CreatedContainers.TryAdd(blobContainer, value: true);
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

        if (_option.CreateContainerIfNotExists)
        {
            await CreateContainerAsync(container, cancellationToken).AnyContext();
        }

        var blobClient = _GetBlobClient(container, blobName);

        var httpHeader = new BlobHttpHeaders
        {
            ContentType = mimeTypeProvider.GetMimeType(blobName),
            CacheControl = _option.CacheControl,
        };

        metadata ??= new Dictionary<string, string?>(StringComparer.Ordinal);
        metadata[_UploadDateMetadataKey] = clock.UtcNow.ToString("O");
        metadata[_ExtensionMetadataKey] = Path.GetExtension(blobName);

        await blobClient.UploadAsync(stream, httpHeader, metadata, cancellationToken: cancellationToken).AnyContext();
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
            MaxDegreeOfParallelism = _option.MaxBulkParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(blobs, options, async (blob, ct) =>
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
        }).AnyContext();

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
        var blobClient = _GetBlobClient(container, blobName);

        var response = await blobClient
            .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken)
            .AnyContext();

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
            return [];
        }

        var batch = blobServiceClient.GetBlobBatchClient();

        var blobUrls = _NormalizeBlobUrls(container, blobNames);

        try
        {
            var results = await batch
                .DeleteBlobsAsync(blobUrls, DeleteSnapshotsOption.IncludeSnapshots, cancellationToken)
                .AnyContext();

            return results.ConvertAll(result => Result<bool, Exception>.Ok(!result.IsError));
        }
        catch (AggregateException e)
            when (e.InnerException is RequestFailedException { Status: 404 } inner
                && string.Equals(inner.ErrorCode, "ContainerNotFound", StringComparison.Ordinal)
            )
        {
            return blobNames.Select(_ => Result<bool, Exception>.Fail(inner)).ToList();
        }
    }

    public async ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    )
    {
        var files = await GetPagedListAsync(container, blobSearchPattern, 500, cancellationToken).AnyContext();
        var count = 0;

        do
        {
            var names = files.Blobs.Select(file => file.BlobKey).ToArray();
            var results = await BulkDeleteAsync(container, names, cancellationToken).AnyContext();
            count += results.Count(x => x.IsSuccess);
            await files.NextPageAsync(cancellationToken).AnyContext();
        } while (files.HasMore);

        logger.LogTrace(
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

        if (_option.CreateContainerIfNotExists)
        {
            await CreateContainerAsync(newBlobContainer, cancellationToken).AnyContext();
        }

        var oldBlobClient = _GetBlobClient(blobContainer, blobName);
        var newBlobClient = _GetBlobClient(newBlobContainer, newBlobName);

        try
        {
            var copyResult = await newBlobClient
                .StartCopyFromUriAsync(oldBlobClient.Uri, cancellationToken: cancellationToken)
                .AnyContext();

            await copyResult.WaitForCompletionAsync(cancellationToken).AnyContext();

            return copyResult.HasCompleted;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return false;
        }
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

        var copyResult = await CopyAsync(blobContainer, blobName, newBlobContainer, newBlobName, cancellationToken)
            .AnyContext();

        if (!copyResult)
        {
            logger.LogWarning("Unable to copy {BlobName} to {NewBlobName}", blobName, newBlobName);

            return false;
        }

        var deleteResult = await DeleteAsync(blobContainer, blobName, cancellationToken).AnyContext();

        if (!deleteResult)
        {
            // Rollback: delete the copy to avoid data duplication
            await DeleteAsync(newBlobContainer, newBlobName, cancellationToken).AnyContext();
            logger.LogWarning("Rename failed for {BlobName}, rolled back copy", blobName);

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
        var blobClient = _GetBlobClient(container, blobName);
        var response = await blobClient.ExistsAsync(cancellationToken).AnyContext();

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
        var blobClient = _GetBlobClient(container, blobName);

        MemoryStream? memoryStream = null;

        try
        {
            memoryStream = new MemoryStream();
            await blobClient.DownloadToAsync(memoryStream, cancellationToken).AnyContext();
            memoryStream.Seek(0, SeekOrigin.Begin);

            return new(memoryStream, blobName);
        }
        catch (RequestFailedException e)
            when (e.ErrorCode == BlobErrorCode.BlobNotFound || e.ErrorCode == BlobErrorCode.ContainerNotFound)
        {
            if (memoryStream is not null)
            {
                await memoryStream.DisposeAsync();
            }

            return null;
        }
        catch
        {
            if (memoryStream is not null)
            {
                await memoryStream.DisposeAsync();
            }
            throw;
        }
    }

    public async ValueTask<BlobInfo?> GetBlobInfoAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        var blobClient = _GetBlobClient(container, blobName);

        Response<BlobProperties>? blobProperties;

        try
        {
            blobProperties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken).AnyContext();
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
            BlobKey = Url.Combine([.. container.Skip(1).Append(blobName)]),
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
        Argument.IsLessThanOrEqualTo(pageSize, int.MaxValue - 1);

        var containerClient = blobServiceClient.GetBlobContainerClient(_GetContainer(container));
        var criteria = _GetRequestCriteria(container.Skip(1), blobSearchPattern);

        var result = new PagedFileListResult(
            async (_, token) =>
                await _GetFilesAsync(containerClient, criteria, pageSize, previousNextPageResult: null, token)
                    .AnyContext()
        );

        await result.NextPageAsync(cancellationToken).AnyContext();

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
            // No more blobs to load.
            if (string.IsNullOrEmpty(previousNextPageResult.ContinuationToken))
            {
                return new AzureNextPageResult
                {
                    Success = true,
                    HasMore = false,
                    Blobs = blobs,
                    ExtraLoadedBlobs = [],
                    ContinuationToken = null,
                    AzureNextPageFunc = null,
                };
            }

            // has the exact number of blobs as the page size
            var remainingBlobsCount = pageSize - blobs.Count;

            if (remainingBlobsCount <= 0)
            {
                return new AzureNextPageResult
                {
                    Success = true,
                    HasMore = remainingBlobsCount != 0,
                    Blobs = blobs.Take(pageSize).ToList(),
                    ExtraLoadedBlobs = blobs.Skip(pageSize).ToList(),
                    ContinuationToken = previousNextPageResult.ContinuationToken,
                    AzureNextPageFunc = (currentResult, token) =>
                        _GetFilesAsync(client, criteria, pageSize, currentResult, token),
                };
            }
        }

        var pageSizeToLoad = pageSize - blobs.Count + 1;
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

        try
        {
            await foreach (var page in pages.WithCancellation(cancellationToken))
            {
                continuationToken = page.ContinuationToken;

                foreach (var blobItem in page.Values)
                {
                    // Check if the blob name matches the pattern.
                    if (criteria.Pattern?.IsMatch(blobItem.Name) == false)
                    {
                        logger.LogTrace("Skipping {Path}: Doesn't match pattern", blobItem.Name);

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
        }
        catch (RequestFailedException e)
            when (e.Status == 404 && string.Equals(e.ErrorCode, "ContainerNotFound", StringComparison.Ordinal))
        {
            return new AzureNextPageResult
            {
                Success = true,
                HasMore = false,
                Blobs = [],
                ExtraLoadedBlobs = [],
                ContinuationToken = null,
                AzureNextPageFunc = null,
            };
        }
        catch (Exception e)
        {
            logger.LogError(
                e,
                "Error while getting blobs from Azure Storage. PageSizeToLoad={PageSizeToLoad}",
                pageSizeToLoad
            );

            throw;
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
                ? (currentResult, token) => _GetFilesAsync(client, criteria, pageSize, currentResult, token)
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


    #region Build URLs


    private BlobClient _GetBlobClient(string[] container, string blobName)
    {
        var (blobContainer, blobPath) = _NormalizeBlob(container, blobName);
        var containerClient = blobServiceClient.GetBlobContainerClient(blobContainer);
        var blobClient = containerClient.GetBlobClient(blobPath);

        return blobClient;
    }

    private List<Uri> _NormalizeBlobUrls(string[] container, IReadOnlyCollection<string> blobNames)
    {
        var prefix =
            blobServiceClient.Uri.AbsoluteUri.EnsureEndsWith('/')
            + container.Select(_NormalizeSlashes).JoinAsString('/');

        return blobNames.Select(blobName => new Uri($"{prefix}/{blobName}")).ToList();
    }

    private static (string Container, string Blob) _NormalizeBlob(string[] containers, string blobName)
    {
        var blob = containers.Skip(1).Append(blobName).Select(_NormalizeSlashes).JoinAsString('/');

        return (_GetContainer(containers), blob);
    }

    private static string _GetContainer(string[] containers)
    {
        return _NormalizeSlashes(containers[0]);
    }

    private static string _NormalizeSlashes(string x)
    {
        return _NormalizePath(x).RemovePostfix('/').RemovePrefix('/');
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
