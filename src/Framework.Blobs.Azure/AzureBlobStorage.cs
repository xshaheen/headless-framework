// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Framework.Abstractions;
using Framework.Blobs.Azure.Internals;
using Framework.Blobs.Internals;
using Framework.Checks;
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
    IBlobNamingNormalizer normalizer,
    ILogger<AzureBlobStorage> logger
) : IBlobStorage
{
    private readonly AzureStorageOptions _option = optionAccessor.Value;

    #region Create Container

    public async ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(container);

        var blobContainer = _GetContainer(container);
        var containerClient = blobServiceClient.GetBlobContainerClient(blobContainer);

        await containerClient
            .CreateIfNotExistsAsync(_option.ContainerPublicAccessType, cancellationToken: cancellationToken)
            .AnyContext();
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
        metadata[BlobStorageHelpers.UploadDateMetadataKey] = clock.UtcNow.ToString("O");
        metadata[BlobStorageHelpers.ExtensionMetadataKey] = Path.GetExtension(blobName);

        if (stream.CanSeek && stream.Position != 0)
        {
            logger.LogWarning(
                "Stream position was {Position}, resetting to 0 for blob {BlobName}",
                stream.Position,
                blobName
            );
            stream.Seek(0, SeekOrigin.Begin);
        }

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

    #region List

    public async IAsyncEnumerable<BlobInfo> GetBlobsAsync(
        string[] container,
        string? blobSearchPattern = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);

        var containerClient = blobServiceClient.GetBlobContainerClient(_GetContainer(container));
        var normalizedDirs = container.Skip(1).Select(_NormalizeContainerName);
        var normalizedPattern = _NormalizeSearchPattern(blobSearchPattern);
        var criteria = BlobStorageHelpers.GetRequestCriteria(normalizedDirs, normalizedPattern);

        await foreach (
            var blobItem in containerClient.GetBlobsAsync(
                traits: BlobTraits.Metadata,
                states: BlobStates.None,
                prefix: criteria.Prefix,
                cancellationToken: cancellationToken
            )
        )
        {
            if (criteria.Pattern?.IsMatch(blobItem.Name) == false)
            {
                continue;
            }

            if (blobItem.Properties.ContentLength is not > 0)
            {
                continue;
            }

            yield return new BlobInfo
            {
                BlobKey = blobItem.Name,
                Size = blobItem.Properties.ContentLength.Value,
                Created = blobItem.Properties.CreatedOn ?? DateTimeOffset.MinValue,
                Modified = blobItem.Properties.LastModified ?? DateTimeOffset.MinValue,
            };
        }
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

        var containerClient = blobServiceClient.GetBlobContainerClient(_GetContainer(container));
        var normalizedDirs = container.Skip(1).Select(_NormalizeContainerName);
        var normalizedPattern = _NormalizeSearchPattern(blobSearchPattern);
        var criteria = BlobStorageHelpers.GetRequestCriteria(normalizedDirs, normalizedPattern);

        var result = new PagedFileListResult(
            async (_, token) =>
                await _GetFilesAsync(containerClient, criteria, pageSize, previous: null, token).AnyContext()
        );

        await result.NextPageAsync(cancellationToken).AnyContext();

        return result;
    }

    private async Task<AzureNextPageResult> _GetFilesAsync(
        BlobContainerClient client,
        SearchCriteria criteria,
        int pageSize,
        AzureNextPageResult? previous = null,
        CancellationToken cancellationToken = default
    )
    {
        var blobs = new List<BlobInfo>();

        // Start with the extra blob from the previous page if present.
        if (previous?.ExtraBlob is not null)
        {
            blobs.Add(previous.ExtraBlob);
        }

        var pageSizeToLoad = pageSize < int.MaxValue ? pageSize + 1 : pageSize;
        var continuationToken = previous?.ContinuationToken;

        // Only fetch from Azure if we need more blobs.
        if (blobs.Count < pageSizeToLoad && (previous is null || !string.IsNullOrEmpty(continuationToken)))
        {
            var pages = client
                .GetBlobsAsync(
                    traits: BlobTraits.Metadata,
                    states: BlobStates.None,
                    prefix: criteria.Prefix,
                    cancellationToken: cancellationToken
                )
                .AsPages(continuationToken, pageSizeToLoad - blobs.Count);

            // AsPages parameter pageSizeHint is not guaranteed to be respected.
            // The service may return fewer results due to partition boundaries.

            try
            {
                await foreach (var page in pages.WithCancellation(cancellationToken))
                {
                    continuationToken = page.ContinuationToken;

                    foreach (var blobItem in page.Values)
                    {
                        if (criteria.Pattern?.IsMatch(blobItem.Name) == false)
                        {
                            logger.LogTrace("Skipping {Path}: Doesn't match pattern", blobItem.Name);
                            continue;
                        }

                        if (blobItem.Properties.ContentLength is not > 0)
                        {
                            continue;
                        }

                        blobs.Add(
                            new BlobInfo
                            {
                                BlobKey = blobItem.Name,
                                Size = blobItem.Properties.ContentLength.Value,
                                Created = blobItem.Properties.CreatedOn ?? DateTimeOffset.MinValue,
                                Modified = blobItem.Properties.LastModified ?? DateTimeOffset.MinValue,
                            }
                        );

                        if (blobs.Count >= pageSizeToLoad)
                        {
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(page.ContinuationToken) || blobs.Count >= pageSizeToLoad)
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
                    AzureNextPageFunc = null,
                };
            }
            catch (Exception e)
            {
                logger.LogError(
                    e,
                    "Error getting blobs from Azure Storage. PageSizeToLoad={PageSizeToLoad}",
                    pageSizeToLoad
                );
                throw;
            }
        }

        var hasMore = blobs.Count > pageSize;
        BlobInfo? extraBlob = null;

        if (hasMore)
        {
            extraBlob = blobs[^1];
            blobs.RemoveAt(blobs.Count - 1);
        }

        return new AzureNextPageResult
        {
            Success = true,
            HasMore = hasMore,
            Blobs = blobs,
            ExtraBlob = extraBlob,
            ContinuationToken = continuationToken,
            AzureNextPageFunc = hasMore
                ? (currentResult, token) => _GetFilesAsync(client, criteria, pageSize, currentResult, token)
                : null,
        };
    }

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
        PathValidation.ValidateContainer(container);
        foreach (var blobName in blobNames)
        {
            PathValidation.ValidatePathSegment(blobName);
        }

        var sb = new StringBuilder(blobServiceClient.Uri.AbsoluteUri);
        if (sb[^1] != '/')
            sb.Append('/');

        for (var i = 0; i < container.Length; i++)
        {
            if (i > 0)
                sb.Append('/');
            sb.Append(_NormalizeContainerName(container[i]));
        }

        var prefix = sb.ToString();
        var result = new List<Uri>(blobNames.Count);

        foreach (var blobName in blobNames)
        {
            result.Add(new Uri($"{prefix}/{_NormalizeSlashes(normalizer.NormalizeBlobName(blobName))}"));
        }

        return result;
    }

    private (string Container, string Blob) _NormalizeBlob(string[] container, string blobName)
    {
        PathValidation.ValidateContainer(container);
        PathValidation.ValidatePathSegment(blobName);

        var normalizedBlobName = normalizer.NormalizeBlobName(blobName);

        var sb = new StringBuilder();
        for (var i = 1; i < container.Length; i++)
        {
            if (sb.Length > 0)
                sb.Append('/');
            sb.Append(_NormalizeContainerName(container[i]));
        }
        if (sb.Length > 0)
            sb.Append('/');
        sb.Append(_NormalizeSlashes(normalizedBlobName));

        return (_GetContainer(container), sb.ToString());
    }

    private string _GetContainer(string[] container)
    {
        PathValidation.ValidateContainer(container);
        return _NormalizeContainerName(container[0]);
    }

    private string _NormalizeContainerName(string containerName)
    {
        return _NormalizeSlashes(normalizer.NormalizeContainerName(containerName));
    }

    private static string _NormalizeSlashes(string x)
    {
        return BlobStorageHelpers.NormalizePath(x).RemovePostfix('/').RemovePrefix('/');
    }

    /// <summary>
    /// Normalizes the search pattern's directory segments to match how they're stored.
    /// E.g., "x\*.txt" becomes "x00/*.txt" because "x" is normalized to "x00".
    /// Only normalizes directory segments, not the final filename/pattern segment.
    /// </summary>
    private string? _NormalizeSearchPattern(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return pattern;
        }

        // First normalize slashes
        pattern = BlobStorageHelpers.NormalizePath(pattern);

        // Split by '/' and normalize directory segments only (not the last segment)
        var segments = pattern.Split('/');
        if (segments.Length <= 1)
        {
            // No directory segments, just a filename/pattern - don't normalize
            return pattern;
        }

        // Normalize all segments except the last one (which is the filename/pattern)
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            // Don't normalize segments containing wildcards
            if (!segment.Contains('*', StringComparison.Ordinal))
            {
                segments[i] = normalizer.NormalizeContainerName(segment);
            }
        }

        return string.Join('/', segments);
    }

    #endregion

    #region Dispose

    public void Dispose() { }

    #endregion
}
