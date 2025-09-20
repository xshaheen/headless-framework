// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Pipelines;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Framework.Tus.Helpers;
using Framework.Tus.Locks;
using Framework.Tus.Models;
using Framework.Tus.Options;
using Framework.Tus.Services;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Framework.Tus;

public sealed class TusAzureStore
    : ITusPipelineStore,
        ITusCreationStore,
        ITusTerminationStore,
        ITusExpirationStore,
        ITusReadableStore,
        ITusCreationDeferLengthStore,
        IDisposable
{
    private readonly TusAzureStoreOptions _options;
    private readonly BlobContainerClient _containerClient;
    private readonly AzureBlobHelper _blobHelper;
    private readonly ITusFileLockProvider _lockProvider;
    private readonly AppendBlobHandler _appendBlobHandler;
    private readonly BlockBlobHandler _blockBlobHandler;
    private readonly ILogger<TusAzureStore> _logger;

    public TusAzureStore(TusAzureStoreOptions options, ILogger<TusAzureStore> logger, ILoggerFactory loggerFactory)
    {
        _options = options;

        _logger = logger;

        var blobServiceClient = new BlobServiceClient(_options.ConnectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(_options.ContainerName);

        _blobHelper = new AzureBlobHelper(loggerFactory.CreateLogger<AzureBlobHelper>());

        // Create specialized handlers
        _appendBlobHandler = new AppendBlobHandler(_options, loggerFactory.CreateLogger<AppendBlobHandler>());
        _blockBlobHandler = new BlockBlobHandler(_options, _blobHelper, loggerFactory.CreateLogger<BlockBlobHandler>());

        // Create lock provider
        var lockLogger = loggerFactory.CreateLogger<AzureBlobFileLock>();
        _lockProvider = new AzureBlobFileLockProvider(_containerClient, _options, lockLogger);

        _InitializeAsync().GetAwaiter().GetResult();
    }

    private async Task _InitializeAsync()
    {
        if (_options.CreateContainerIfNotExists)
        {
            try
            {
                await _containerClient.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.None);
                _logger.LogInformation("Initialized Azure Blob container: {ContainerName}", _options.ContainerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize container: {ContainerName}", _options.ContainerName);

                throw;
            }
        }
    }

    #region ITusStore Implementation

    public async Task<bool> FileExistAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobClient = _GetBlobClient(fileId);

        return await _blobHelper.BlobExistsAsync(blobClient, cancellationToken);
    }

    public async Task<long?> GetUploadLengthAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobInfo = await _GetBlobInfoAsync(fileId, cancellationToken);

        return blobInfo != null
            ? MetadataHelper.GetUploadLength(blobInfo.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value))
            : null;
    }

    public async Task<long> GetUploadOffsetAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobInfo = await _GetBlobInfoAsync(fileId, cancellationToken);

        if (blobInfo == null)
            return 0;

        var blobType = MetadataHelper.GetBlobType(blobInfo.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

        return blobType switch
        {
            BlobType.AppendBlob => await _GetAppendBlobOffset(fileId, cancellationToken),
            BlobType.BlockBlob => await _GetBlockBlobOffset(fileId, cancellationToken),
            _ => 0,
        };
    }

    public async Task<long> AppendDataAsync(string fileId, Stream data, CancellationToken cancellationToken)
    {
        var blobInfo = await _GetBlobInfoAsync(fileId, cancellationToken);

        if (blobInfo == null)
        {
            throw new InvalidOperationException($"File {fileId} does not exist");
        }

        var blobType = MetadataHelper.GetBlobType(blobInfo.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

        // Get lock for this file
        var fileLock = await _lockProvider.AquireLock(fileId);
        var lockAcquired = await fileLock.Lock();

        if (!lockAcquired)
        {
            throw new InvalidOperationException(
                $"Unable to acquire lock for file {fileId}. Another upload might be in progress."
            );
        }

        try
        {
            long bytesWritten = blobType switch
            {
                BlobType.AppendBlob => await _AppendToAppendBlob(fileId, data, cancellationToken),
                BlobType.BlockBlob => await _AppendToBlockBlob(fileId, data, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported blob type: {blobType}"),
            };

            // Update metadata with new block count if needed
            await _UpdateBlobMetadataAfterAppend(fileId, blobType, cancellationToken);

            return bytesWritten;
        }
        finally
        {
            await fileLock.ReleaseIfHeld();
        }
    }

    #endregion

    #region ITusPipelineStore Implementation

    public async Task<long> AppendDataAsync(string fileId, PipeReader pipeReader, CancellationToken cancellationToken)
    {
        // Convert PipeReader to Stream for processing
        var stream = pipeReader.AsStream();

        return await AppendDataAsync(fileId, stream, cancellationToken);
    }

    #endregion

    #region ITusCreationStore Implementation

    public async Task<string> CreateFileAsync(long uploadLength, string? metadata, CancellationToken cancellationToken)
    {
        var fileId = Guid.NewGuid().ToString();

        try
        {
            // Parse TUS metadata
            var tusMetadata = _ParseMetadata(metadata);

            // Determine blob type based on strategy
            var blobType = BlobStrategyHelper.DetermineBlobType(_options, uploadLength, tusMetadata);

            // Create blob metadata
            var blobMetadata = MetadataHelper.EncodeTusMetadata(tusMetadata);
            MetadataHelper.SetUploadLength(blobMetadata, uploadLength);
            MetadataHelper.SetCreatedDate(blobMetadata, DateTimeOffset.UtcNow);
            MetadataHelper.SetBlobType(blobMetadata, blobType);
            MetadataHelper.SetBlockCount(blobMetadata, 0);

            // Create the actual blob based on type
            await _CreateBlobByType(fileId, blobType, blobMetadata, cancellationToken);

            _logger.LogInformation(
                "Created file {FileId} with upload length {UploadLength} using {BlobType}",
                fileId,
                uploadLength,
                blobType
            );

            return fileId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create file with upload length {UploadLength}", uploadLength);

            throw;
        }
    }

    public async Task<string?> GetUploadMetadataAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobInfo = await _GetBlobInfoAsync(fileId, cancellationToken);

        if (blobInfo == null)
            return null;

        var tusMetadata = MetadataHelper.DecodeTusMetadata(
            blobInfo.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        );

        return _SerializeMetadata(tusMetadata);
    }

    #endregion

    #region ITusCreationDeferLengthStore Implementation

    public async Task SetUploadLengthAsync(string fileId, long uploadLength, CancellationToken cancellationToken)
    {
        // Get lock for this file
        var fileLock = await _lockProvider.AquireLock(fileId);
        var lockAcquired = await fileLock.Lock();

        if (!lockAcquired)
        {
            throw new InvalidOperationException($"Unable to acquire lock for file {fileId} to set upload length.");
        }

        try
        {
            var blobClient = _GetBlobClient(fileId);
            var blobInfo = await _blobHelper.GetBlobInfoAsync(blobClient, cancellationToken);

            if (blobInfo == null)
            {
                throw new InvalidOperationException($"File {fileId} does not exist");
            }

            // Check if we need to change blob type based on the now-known length
            var metadataDict = blobInfo.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var currentBlobType = MetadataHelper.GetBlobType(metadataDict);
            var tusMetadata = MetadataHelper.DecodeTusMetadata(metadataDict);
            var optimalBlobType = BlobStrategyHelper.DetermineBlobType(_options, uploadLength, tusMetadata);

            if (optimalBlobType != currentBlobType)
            {
                // Need to migrate blob type - this is complex, for now log a warning
                _logger.LogWarning(
                    "Optimal blob type for file {FileId} would be {OptimalType} but currently using {CurrentType}. Consider adjusting strategy.",
                    fileId,
                    optimalBlobType,
                    currentBlobType
                );
            }

            // Update metadata
            var updatedMetadata = new Dictionary<string, string>(blobInfo.Metadata);
            MetadataHelper.SetUploadLength(updatedMetadata, uploadLength);

            await blobClient.SetMetadataAsync(updatedMetadata, cancellationToken: cancellationToken);

            _logger.LogDebug("Set upload length for file {FileId} to {UploadLength}", fileId, uploadLength);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set upload length for file {FileId}", fileId);

            throw;
        }
        finally
        {
            await fileLock.ReleaseIfHeld();
        }
    }

    #endregion

    #region ITusTerminationStore Implementation

    public async Task DeleteFileAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobClient = _GetBlobClient(fileId);
        var deleted = await _blobHelper.DeleteBlobIfExistsAsync(blobClient, cancellationToken);

        if (deleted)
        {
            _logger.LogInformation("Deleted file {FileId}", fileId);
        }
        else
        {
            _logger.LogWarning("File {FileId} was not found for deletion", fileId);
        }
    }

    #endregion

    #region ITusExpirationStore Implementation

    public async Task SetExpirationAsync(string fileId, DateTimeOffset expires, CancellationToken cancellationToken)
    {
        var blobClient = _GetBlobClient(fileId);

        try
        {
            var blobInfo = await _blobHelper.GetBlobInfoAsync(blobClient, cancellationToken);

            if (blobInfo == null)
            {
                _logger.LogWarning("Cannot set expiration for non-existent file {FileId}", fileId);

                return;
            }

            var metadata = new Dictionary<string, string>(blobInfo.Metadata);
            MetadataHelper.SetExpirationDate(metadata, expires);

            await blobClient.SetMetadataAsync(metadata, cancellationToken: cancellationToken);
            _logger.LogDebug("Set expiration for file {FileId} to {ExpirationDate}", fileId, expires);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set expiration for file {FileId}", fileId);

            throw;
        }
    }

    public async Task<DateTimeOffset?> GetExpirationAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobInfo = await _GetBlobInfoAsync(fileId, cancellationToken);

        return blobInfo != null
            ? MetadataHelper.GetExpirationDate(blobInfo.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value))
            : null;
    }

    public async Task<IEnumerable<string>> GetExpiredFilesAsync(CancellationToken cancellationToken)
    {
        var expiredFiles = new List<string>();
        var now = DateTimeOffset.UtcNow;

        try
        {
            await foreach (
                var blobItem in _containerClient.GetBlobsAsync(
                    traits: Azure.Storage.Blobs.Models.BlobTraits.Metadata,
                    prefix: _options.BlobPrefix,
                    cancellationToken: cancellationToken
                )
            )
            {
                var expirationDate = MetadataHelper.GetExpirationDate(
                    blobItem.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                );

                if (expirationDate.HasValue && expirationDate.Value <= now)
                {
                    var fileId = _ExtractFileIdFromBlobName(blobItem.Name);

                    if (!string.IsNullOrEmpty(fileId))
                    {
                        expiredFiles.Add(fileId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get expired files");

            throw;
        }

        return expiredFiles;
    }

    public async Task<int> RemoveExpiredFilesAsync(CancellationToken cancellationToken)
    {
        var expiredFiles = await GetExpiredFilesAsync(cancellationToken);
        var removedCount = 0;

        foreach (var fileId in expiredFiles)
        {
            try
            {
                await DeleteFileAsync(fileId, cancellationToken);
                removedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove expired file {FileId}", fileId);
            }
        }

        _logger.LogInformation("Removed {RemovedCount} expired files", removedCount);

        return removedCount;
    }

    #endregion

    #region ITusReadableStore Implementation

    public async Task<ITusFile?> GetFileAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobClient = _GetBlobClient(fileId);
        var blobInfo = await _blobHelper.GetBlobInfoAsync(blobClient, cancellationToken);

        if (blobInfo == null)
            return null;

        var tusFile = new TusAzureFile
        {
            FileId = fileId,
            BlobName = blobInfo.BlobName,
            UploadLength = MetadataHelper.GetUploadLength(
                blobInfo.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ),
            UploadOffset = blobInfo.Size,
            Metadata = MetadataHelper.DecodeTusMetadata(
                blobInfo.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ),
            ExpirationDate = MetadataHelper.GetExpirationDate(
                blobInfo.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ),
            CreatedDate = MetadataHelper.GetCreatedDate(
                blobInfo.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ),
            LastModified = blobInfo.LastModified,
        };

        return new TusAzureFileWrapper(tusFile, blobClient, _logger);
    }

    #endregion

    #region Private Helper Methods

    private async Task _CreateBlobByType(
        string fileId,
        BlobType blobType,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken
    )
    {
        var blobClient = _GetBlobClient(fileId);

        switch (blobType)
        {
            case BlobType.AppendBlob:
                var appendBlobClient = _containerClient.GetAppendBlobClient(
                    _blobHelper.GetBlobName(fileId, _options.BlobPrefix)
                );
                await _appendBlobHandler.CreateBlobAsync(appendBlobClient, metadata, cancellationToken);

                break;

            case BlobType.BlockBlob:
                var blockBlobClient = _containerClient.GetBlockBlobClient(
                    _blobHelper.GetBlobName(fileId, _options.BlobPrefix)
                );
                await _blockBlobHandler.CreateBlobAsync(blockBlobClient, metadata, cancellationToken);

                break;

            default:
                throw new InvalidOperationException($"Unsupported blob type: {blobType}");
        }
    }

    private async Task<long> _AppendToAppendBlob(string fileId, Stream data, CancellationToken cancellationToken)
    {
        var appendBlobClient = _containerClient.GetAppendBlobClient(
            _blobHelper.GetBlobName(fileId, _options.BlobPrefix)
        );

        return await _appendBlobHandler.AppendDataAsync(appendBlobClient, data, cancellationToken);
    }

    private async Task<long> _AppendToBlockBlob(string fileId, Stream data, CancellationToken cancellationToken)
    {
        var blockBlobClient = _containerClient.GetBlockBlobClient(_blobHelper.GetBlobName(fileId, _options.BlobPrefix));

        // Create a minimal BlobInfo for the handler
        var blobInfo = new TusAzureBlobInfo { FileId = fileId, Type = BlobType.BlockBlob };

        return await _blockBlobHandler.AppendDataAsync(blockBlobClient, data, blobInfo, cancellationToken);
    }

    private async Task<long> _GetAppendBlobOffset(string fileId, CancellationToken cancellationToken)
    {
        var appendBlobClient = _containerClient.GetAppendBlobClient(
            _blobHelper.GetBlobName(fileId, _options.BlobPrefix)
        );

        return await _appendBlobHandler.GetUploadOffsetAsync(appendBlobClient, cancellationToken);
    }

    private async Task<long> _GetBlockBlobOffset(string fileId, CancellationToken cancellationToken)
    {
        var blockBlobClient = _containerClient.GetBlockBlobClient(_blobHelper.GetBlobName(fileId, _options.BlobPrefix));

        return await _blockBlobHandler.GetUploadOffsetAsync(blockBlobClient, cancellationToken);
    }

    private async Task _UpdateBlobMetadataAfterAppend(
        string fileId,
        BlobType blobType,
        CancellationToken cancellationToken
    )
    {
        if (blobType == BlobType.BlockBlob)
        {
            // Update block count in metadata for block blobs
            var blobClient = _GetBlobClient(fileId);
            var blobInfo = await _blobHelper.GetBlobInfoAsync(blobClient, cancellationToken);

            if (blobInfo != null)
            {
                var blockList = await _blobHelper.GetCommittedBlockListAsync(blobClient, cancellationToken);
                var metadata = new Dictionary<string, string>(blobInfo.Metadata);
                MetadataHelper.SetBlockCount(metadata, blockList.Count);

                await blobClient.SetMetadataAsync(metadata, cancellationToken: cancellationToken);
            }
        }
    }

    private BlobClient _GetBlobClient(string fileId)
    {
        var blobName = _blobHelper.GetBlobName(fileId, _options.BlobPrefix);

        return _containerClient.GetBlobClient(blobName);
    }

    private async Task<AzureBlobInfo?> _GetBlobInfoAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobClient = _GetBlobClient(fileId);

        return await _blobHelper.GetBlobInfoAsync(blobClient, cancellationToken);
    }

    private string _ExtractFileIdFromBlobName(string blobName)
    {
        var prefix = _options.BlobPrefix.TrimEnd('/') + "/";

        return blobName.StartsWith(prefix) ? blobName[prefix.Length..] : string.Empty;
    }

    private Dictionary<string, string> _ParseMetadata(string? metadata)
    {
        var result = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(metadata))
            return result;

        // TUS metadata format: key1 base64value1,key2 base64value2
        var pairs = metadata.Split(',');

        foreach (var pair in pairs)
        {
            var spaceIndex = pair.IndexOf(' ');

            if (spaceIndex > 0)
            {
                var key = pair[..spaceIndex];
                var base64Value = pair[(spaceIndex + 1)..];

                try
                {
                    var value = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64Value));
                    result[key] = value;
                }
                catch (FormatException)
                {
                    // Skip invalid base64 values
                    _logger.LogWarning("Invalid base64 metadata value for key: {Key}", key);
                }
            }
        }

        return result;
    }

    private string _SerializeMetadata(Dictionary<string, string> metadata)
    {
        if (!metadata.Any())
            return string.Empty;

        var parts = metadata.Select(kvp =>
        {
            var base64Value = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(kvp.Value));

            return $"{kvp.Key} {base64Value}";
        });

        return string.Join(",", parts);
    }

    #endregion

    #region IDisposable Implementation

    public void Dispose()
    {
        _logger.LogDebug("TusAzureStore disposed");
    }

    #endregion
}
