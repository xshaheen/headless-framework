// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Flurl;
using Framework.Checks;
using Framework.Constants;
using Framework.Core;
using Framework.Primitives;
using Framework.Serializer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Framework.Blobs.Redis;

public sealed class RedisBlobStorage : IBlobStorage
{
    private readonly ILogger _logger;
    private readonly ISerializer _serializer;
    private readonly RedisBlobStorageOptions _options;

    public IDatabase Database => _options.ConnectionMultiplexer.GetDatabase();

    public RedisBlobStorage(IOptions<RedisBlobStorageOptions> optionsAccessor)
    {
        _options = optionsAccessor.Value;
        _logger = _options.LoggerFactory?.CreateLogger(typeof(RedisBlobStorage)) ?? NullLogger.Instance;
        _serializer = _options.Serializer ?? new SystemJsonSerializer();
    }

    #region Create Container

    public ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(container);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.CompletedTask;
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

        var (blobsContainer, infoContainer) = _BuildContainerPath(container);
        var blobPath = _BuildBlobPath(container, blobName);

        try
        {
            var database = Database;

            await using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, 0x14000, cancellationToken).AnyContext();
            var saveBlobTask = database.HashSetAsync(blobsContainer, blobPath, memory.ToArray());
            var fileSize = memory.Length;
            memory.Seek(0, SeekOrigin.Begin);
            memory.SetLength(0);

            var blobInfo = new BlobInfo
            {
                BlobKey = blobPath,
                Created = DateTimeOffset.UtcNow,
                Modified = DateTimeOffset.UtcNow,
                Size = fileSize,
            };

            _serializer.Serialize(blobInfo, memory);

            var saveInfoTask = database.HashSetAsync(infoContainer, blobPath, memory.ToArray());

            await Run.WithRetriesAsync(
                async () => await Task.WhenAll(saveBlobTask, saveInfoTask).WithAggregatedExceptions().AnyContext(),
                logger: _logger,
                cancellationToken: cancellationToken
            ).AnyContext();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error saving {Path}: {Message}", blobPath, e.Message);

            throw;
        }
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

        var tasks = blobs.Select(
            async blob =>
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
            }
        );

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
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        var (blobsContainer, infoContainer) = _BuildContainerPath(container);
        var blobPath = _BuildBlobPath(container, blobName);

        return await _DeleteAsync(blobPath, infoContainer, blobsContainer, cancellationToken);
    }

    private async Task<bool> _DeleteAsync(string blobPath, string infoContainer, string blobsContainer, CancellationToken cancellationToken)
    {
        _logger.LogTrace("Deleting {Path}", blobPath);

        var database = Database;
        var deleteInfoTask = database.HashDeleteAsync(infoContainer, blobPath);
        var deleteBlobTask = database.HashDeleteAsync(blobsContainer, blobPath);


        var result = await Run.WithRetriesAsync(
            async () => await Task.WhenAll(deleteInfoTask, deleteBlobTask).WithAggregatedExceptions().AnyContext(),
            logger: _logger,
            cancellationToken: cancellationToken
        ).AnyContext();

        return result[0] || result[1];
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

        var tasks = blobNames.Select(
            async fileName =>
            {
                try
                {
                    return await DeleteAsync(container, fileName, cancellationToken);
                }
                catch (Exception e)
                {
                    return Result<bool, Exception>.Fail(e);
                }
            }
        );

        return await Task.WhenAll(tasks).WithAggregatedExceptions();
    }

    public async ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    )
    {
        var blobs = await _GetFileListAsync(container, blobSearchPattern, cancellationToken: cancellationToken).AnyContext();

        _logger.LogInformation("Deleting {FileCount} files matching {SearchPattern}", blobs, blobSearchPattern);

        var (blobsContainer, infoContainer) = _BuildContainerPath(container);

        var tasks = blobs.Select(
            async blob =>
            {
                try
                {
                  return await _DeleteAsync(blob.BlobKey, infoContainer, blobsContainer, cancellationToken);
                }
                catch (Exception e)
                {
                    return Result<bool, Exception>.Fail(e);
                }
            }
        );

        var results = await Task.WhenAll(tasks).WithAggregatedExceptions();
        var count = results.Count(r => r.Succeeded);

        _logger.LogTrace("Finished deleting {FileCount} files matching {SearchPattern}", count, blobSearchPattern);

        return count;
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
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(blobContainer);
        Argument.IsNotNull(newBlobName);
        Argument.IsNotNull(newBlobContainer);

        var srcBlobPath = _BuildBlobPath(blobContainer, blobName);
        var dstBlobPath = _BuildBlobPath(newBlobContainer, newBlobName);
        _logger.LogInformation("Renaming {Path} to {NewPath}", srcBlobPath, dstBlobPath);

        try
        {
            var result = await CopyAsync(blobContainer, blobName, newBlobContainer, newBlobName, cancellationToken);

            if (!result)
            {
                return false;
            }

            return await DeleteAsync(blobContainer, blobName, cancellationToken).AnyContext();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error renaming {Path} to {NewPath}: {Message}", srcBlobPath, dstBlobPath, e.Message);

            return false;
        }
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
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(blobContainer);
        Argument.IsNotNull(newBlobName);
        Argument.IsNotNull(newBlobContainer);

        try
        {
            var result = await DownloadAsync(blobContainer, blobName, cancellationToken).AnyContext();

            if (result is null)
            {
                return false;
            }

            await using var stream = result.Stream;
            await UploadAsync(newBlobContainer, newBlobName, stream, metadata: null, cancellationToken).AnyContext();

            return true;
        }
        catch (Exception e)
        {
            var srcBlobPath = _BuildBlobPath(blobContainer, blobName);
            var dstBlobPath = _BuildBlobPath(newBlobContainer, newBlobName);
            _logger.LogError(e, "Error copying {Path} to {TargetPath}: {Message}", srcBlobPath, dstBlobPath, e.Message);

            return false;
        }
    }

    #endregion

    #region Exists

    public async ValueTask<bool> ExistsAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        var (_, infoContainer) = _BuildContainerPath(container);
        var blobPath = _BuildBlobPath(container, blobName);

        _logger.LogTrace("Checking if {Path} exists", blobPath);

        return await Run.WithRetriesAsync(() => Database.HashExistsAsync(infoContainer, blobPath), logger: _logger, cancellationToken: cancellationToken);
    }

    #endregion

    #region Downalod

    public async ValueTask<BlobDownloadResult?> DownloadAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        var (blobsContainer, _) = _BuildContainerPath(container);
        var blobPath = _BuildBlobPath(container, blobName);

        _logger.LogTrace("Getting file stream for {Path}", blobPath);

        var fileContent = await Run.WithRetriesAsync(
            () => Database.HashGetAsync(blobsContainer, blobPath),
            logger: _logger,
            cancellationToken: cancellationToken
        ).AnyContext();

        if (fileContent.IsNull)
        {
            _logger.LogError("Unable to get file stream for {Path}: File Not Found", blobPath);

            return null;
        }

        var stream = new MemoryStream(fileContent!);

        return new BlobDownloadResult(stream, blobName);
    }

    public async ValueTask<BlobInfo?> GetBlobInfoAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);
        Argument.IsNotNull(blobName);

        var (_, infoContainer) = _BuildContainerPath(container);
        var blobPath = _BuildBlobPath(container, blobName);
        _logger.LogTrace("Getting file info for {Path}", blobPath);

        var blobInfo = await Run.WithRetriesAsync(
            (Database, infoContainer, blobPath),
            static state => state.Database.HashGetAsync(state.infoContainer, state.blobPath),
            cancellationToken: cancellationToken
        );

        if (!blobInfo.HasValue)
        {
            _logger.LogError("Unable to get file info for {Path}: File Not Found", blobPath);

            return null;
        }

        return _serializer.Deserialize<BlobInfo>((byte[]) blobInfo!);
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

        var (_, infoContainer) = _BuildContainerPath(container);
        var criteria = _GetRequestCriteria(container.Skip(1), blobSearchPattern);

        var result = new PagedFileListResult((_, token) => _GetFilesPageAsync(infoContainer, criteria, 1, pageSize, token));
        await result.NextPageAsync(cancellationToken).AnyContext();

        return result;
    }

    private async ValueTask<INextPageResult> _GetFilesPageAsync(
        string container,
        SearchCriteria criteria,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        var pagingLimit = pageSize;
        var skip = (page - 1) * pagingLimit;

        if (pagingLimit < int.MaxValue)
        {
            pagingLimit++;
        }

        _logger.LogTrace(
            s => s.Property("Limit", pagingLimit).Property("Skip", skip),
            "Getting files matching {Prefix} and {Pattern}...",
            criteria.Prefix,
            criteria.Pattern
        );

        var list = await _ScanBlobInfoListAsync(container, criteria, skip, pagingLimit, cancellationToken);

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
            Blobs = list,
            NextPageFunc = hasMore ? (_, token) => _GetFilesPageAsync(container, criteria, page + 1, pageSize, token) : null,
        };
    }

    private async Task<List<BlobInfo>> _GetFileListAsync(
        string[] container,
        string? searchPattern = null,
        int? limit = null,
        int? skip = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(limit);
        Argument.IsPositiveOrZero(skip);

        var (_, infoContainer) = _BuildContainerPath(container);
        var criteria = _GetRequestCriteria(container.Skip(1), searchPattern);

        var pageSize = limit ?? int.MaxValue;

        _logger.LogTrace(
            s => s.Property("SearchPattern", searchPattern).Property("Limit", limit).Property("Skip", skip),
            "Getting file list matching {Prefix} and {Pattern}...",
            criteria.Prefix,
            criteria.Pattern
        );

        var blobs = await _ScanBlobInfoListAsync(infoContainer, criteria, skip ?? 0, pageSize, cancellationToken);

        return blobs;
    }

    private async Task<List<BlobInfo>> _ScanBlobInfoListAsync(
        string container,
        SearchCriteria criteria,
        int skipCount = 0,
        int? pagingLimit = null,
        CancellationToken cancellationToken = default
    )
    {
        List<BlobInfo> list = [];

        await foreach (var hashEntry in Database.HashScanAsync(container, $"{criteria.Prefix}*").WithCancellation(cancellationToken))
        {
            if (hashEntry.Value.IsNull)
            {
                continue;
            }

            var blobInfo = _serializer.Deserialize<BlobInfo>((byte[]) hashEntry.Value!)!;

            if (criteria.Pattern != null && !criteria.Pattern.IsMatch(blobInfo.BlobKey))
            {
                continue;
            }

            // Skip
            if (skipCount > 0)
            {
                skipCount--;

                continue;
            }

            // Take
            if (pagingLimit.HasValue && list.Count == pagingLimit)
            {
                break;
            }

            list.Add(blobInfo);
        }

        return list;
    }

    private static SearchCriteria _GetRequestCriteria(IEnumerable<string> directories, string? searchPattern)
    {
        searchPattern = Url.Combine(string.Join('/', directories), _NormalizePath(searchPattern));

        if (string.IsNullOrEmpty(searchPattern))
        {
            return new SearchCriteria();
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

        return new(prefix, patternRegex);
    }

    private sealed record SearchCriteria(string Prefix = "", Regex? Pattern = null);

    #endregion

    #region Build Paths

    private static (string BlobsContainer, string InfoContainer) _BuildContainerPath(string[] container)
    {
        Argument.IsNotNullOrEmpty(container);

        var path = _NormalizePath(container[0]);

        return (path.EnsureEndsWith('/'), ("blob-info/" + path).EnsureEndsWith('/'));
    }

    private static string _BuildBlobPath(string[] container, string blobName)
    {
        var path =
            container.Length == 1 ? container[0] : string.Join('/', container.Skip(1)).EnsureEndsWith('/') + blobName;

        return _NormalizePath(path);
    }

    [return: NotNullIfNotNull(nameof(path))]
    private static string? _NormalizePath(string? path) => path?.Replace('\\', '/');

    #endregion

    #region Dispose

    public void Dispose() { }

    #endregion
}
