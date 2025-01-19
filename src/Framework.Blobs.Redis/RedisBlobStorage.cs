// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Checks;
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
        throw new NotImplementedException();
    }

    #endregion

    #region Upload

    public ValueTask UploadAsync(
        string[] container,
        string blobName,
        Stream stream,
        Dictionary<string, string?>? metadata = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobName);
        Argument.IsNotNullOrEmpty(container);

        throw new NotImplementedException();
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

    public ValueTask<bool> DeleteAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        throw new NotImplementedException();
    }

    private Task<bool> _DeleteAsync(string blobPath, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
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

        var tasks = blobNames.Select(async fileName =>
        {
            try
            {
                return await DeleteAsync(container, fileName, cancellationToken);
            }
            catch (Exception e)
            {
                return Result<bool, Exception>.Fail(e);
            }
        });

        return await Task.WhenAll(tasks).WithAggregatedExceptions();
    }

    public ValueTask<int> DeleteAllAsync(
        string[] container,
        string? blobSearchPattern = null,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotImplementedException();
    }

    public Task<int> DeleteDirectoryAsync(
        string directory,
        bool includeSelf = true,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation("Deleting {Directory} directory", directory);

        throw new NotImplementedException();
    }

    #endregion

    #region Rename

    public ValueTask<bool> RenameAsync(
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

        throw new NotImplementedException();
    }

    #endregion

    #region Copy

    public ValueTask<bool> CopyAsync(
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

        throw new NotImplementedException();
    }

    #endregion

    #region Exists

    public ValueTask<bool> ExistsAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        throw new NotImplementedException();
    }

    #endregion

    #region Downalod

    public ValueTask<BlobDownloadResult?> DownloadAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        throw new NotImplementedException();

        // if (String.IsNullOrEmpty(path))
        //     throw new ArgumentNullException(nameof(path));
        //
        // if (streamMode is StreamMode.Write)
        //     throw new NotSupportedException($"Stream mode {streamMode} is not supported.");
        //
        // string normalizedPath = NormalizePath(path);
        // _logger.LogTrace("Getting file stream for {Path}", normalizedPath);
        //
        // var fileContent = await Run.WithRetriesAsync(() => Database.HashGetAsync(_options.ContainerName, normalizedPath),
        //     cancellationToken: cancellationToken, logger: _logger).AnyContext();
        //
        // if (fileContent.IsNull)
        // {
        //     _logger.LogError("Unable to get file stream for {Path}: File Not Found", normalizedPath);
        //     return null;
        // }
        //
        // return new MemoryStream(fileContent);
    }

    public async ValueTask<BlobInfo?> GetBlobInfoAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);
        Argument.IsNotNull(blobName);

        var containerPath = _BuildContainerPath(container);
        var blobPath = _BuildBlobPath(container, blobName);
        _logger.LogTrace("Getting file info for {Path}", blobPath);

        var blobInfo = await Run.WithRetriesAsync(
            (Database, containerPath, blobPath),
            static state => state.Database.HashGetAsync(state.containerPath, state.blobPath),
            cancellationToken: cancellationToken
        );

        if (!blobInfo.HasValue)
        {
            _logger.LogError("Unable to get file info for {Path}: File Not Found", blobPath);

            return null;
        }

        return _serializer.Deserialize<BlobInfo>((byte[])blobInfo!);
    }

    #endregion

    #region List

    public ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] container,
        string? blobSearchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(container);
        Argument.IsPositive(pageSize);
        Argument.IsLessThanOrEqualTo(pageSize, int.MaxValue - 1);

        var directoryPath = _BuildContainerPath(container);

        throw new NotImplementedException();
    }

    // private sealed record SearchCriteria(string PathPrefix = "", Regex? Pattern = null);

    #endregion

    #region Build Paths

    private static string _BuildContainerPath(string[] container)
    {
        Argument.IsNotNullOrEmpty(container);

        return _NormalizePath(container[0]).EnsureEndsWith('/');
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
