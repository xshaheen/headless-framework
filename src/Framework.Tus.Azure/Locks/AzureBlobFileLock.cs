// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Framework.Tus.Options;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Framework.Tus.Locks;

public sealed class AzureBlobFileLock(BlobClient blobClient, TimeSpan leaseDuration, ILogger<AzureBlobFileLock> logger)
    : ITusFileLock
{
    private BlobLeaseClient? _leaseClient;
    private bool _isLocked;
    private bool _disposed;

    public async Task<bool> Lock()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isLocked)
        {
            return true; // Already locked by this instance
        }

        try
        {
            // Ensure the blob exists before trying to lease it
            var blobExists = await blobClient.ExistsAsync();

            if (!blobExists.Value)
            {
                logger.LogWarning("Cannot lock non-existent blob: {BlobName}", blobClient.Name);

                return false;
            }

            _leaseClient = blobClient.GetBlobLeaseClient();
            var leaseResponse = await _leaseClient.AcquireAsync(leaseDuration);

            _isLocked = true;
            logger.LogDebug("Successfully acquired lease for blob: {BlobName}", blobClient.Name);

            return true;
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == "LeaseAlreadyPresent")
        {
            logger.LogDebug("Blob {BlobName} is already locked", blobClient.Name);

            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to acquire lock for blob: {BlobName}", blobClient.Name);

            return false;
        }
    }

    public async Task ReleaseIfHeld()
    {
        if (_disposed || !_isLocked || _leaseClient == null)
            return;

        try
        {
            await _leaseClient.ReleaseAsync();
            logger.LogDebug("Successfully released lease for blob: {BlobName}", blobClient.Name);
        }
        catch (RequestFailedException ex)
            when (ex.ErrorCode == "LeaseIdMismatchWithLeaseOperation"
                || ex.ErrorCode == "LeaseNotPresentWithLeaseOperation"
            )
        {
            logger.LogDebug("Lease for blob {BlobName} was already released or expired", blobClient.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to release lease for blob: {BlobName}", blobClient.Name);
        }
        finally
        {
            _isLocked = false;
            _leaseClient = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            // Try to release the lease synchronously during dispose
            if (_isLocked && _leaseClient != null)
            {
                _leaseClient.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to release lease during dispose for blob: {BlobName}", blobClient.Name);
        }
        finally
        {
            _disposed = true;
            _isLocked = false;
            _leaseClient = null;
        }
    }
}

public class AzureBlobFileLockProvider(
    BlobContainerClient containerClient,
    TusAzureStoreOptions options,
    ILogger<AzureBlobFileLock> logger
) : ITusFileLockProvider
{
    public Task<ITusFileLock> AquireLock(string fileId)
    {
        var blobName = _GetBlobName(fileId);
        var blobClient = containerClient.GetBlobClient(blobName);

        return Task.FromResult<ITusFileLock>(new AzureBlobFileLock(blobClient, options.DefaultLeaseTime, logger));
    }

    private string _GetBlobName(string fileId)
    {
        return $"{options.BlobPrefix.TrimEnd('/')}/{fileId}";
    }
}
