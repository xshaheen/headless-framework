// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using Framework.Tus.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Framework.Tus.Services;

public sealed partial class TusAzureStore : ITusExpirationStore
{
    public async Task SetExpirationAsync(string fileId, DateTimeOffset expires, CancellationToken cancellationToken)
    {
        var blobClient = _GetBlobClient(fileId);

        try
        {
            var azureFile = await _GetTusFileInfoAsync(blobClient, fileId, cancellationToken);

            if (azureFile == null)
            {
                _logger.LogWarning("Cannot set expiration for non-existent file {FileId}", fileId);

                return;
            }

            azureFile.Metadata.DateExpiration = expires;
            await _UpdateMetadataAsync(blobClient, azureFile, cancellationToken);

            _logger.LogDebug("Set expiration for file {FileId} to {ExpirationDate}", fileId, expires);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to set expiration for file {FileId}", fileId);

            throw;
        }
    }

    public async Task<DateTimeOffset?> GetExpirationAsync(string fileId, CancellationToken cancellationToken)
    {
        var azureFile = await _GetTusFileInfoAsync(fileId, cancellationToken);

        return azureFile?.Metadata.DateExpiration;
    }

    public async Task<IEnumerable<string>> GetExpiredFilesAsync(CancellationToken cancellationToken)
    {
        var expiredFiles = new List<string>();
        var now = _timeProvider.GetUtcNow();

        try
        {
            await foreach (
                var blobItem in _containerClient.GetBlobsAsync(
                    traits: BlobTraits.Metadata,
                    states: BlobStates.None,
                    prefix: _options.BlobPrefix,
                    cancellationToken: cancellationToken
                )
            )
            {
                var metadata = TusAzureMetadata.FromAzure(blobItem.Metadata);

                if (metadata.DateExpiration <= now)
                {
                    var fileId = _ExtractFileIdFromBlobName(blobItem.Name);

                    if (!string.IsNullOrEmpty(fileId))
                    {
                        expiredFiles.Add(fileId);
                    }
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to get expired files");

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
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to remove expired file {FileId}", fileId);
            }
        }

        _logger.LogInformation("Removed {RemovedCount} expired files", removedCount);

        return removedCount;
    }
}
