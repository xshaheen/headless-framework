// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Framework.Tus.Locks;

public sealed class AzureBlobFileLock(BlobClient blobClient, TimeSpan leaseDuration, ILogger<AzureBlobFileLock> logger)
    : ITusFileLock
{
    private BlobLeaseClient? _leaseClient;
    private bool _isLocked;

    public async Task<bool> Lock()
    {
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
            await _leaseClient.AcquireAsync(leaseDuration);

            _isLocked = true;
            logger.LogDebug("Successfully acquired lease for blob: {BlobName}", blobClient.Name);

            return true;
        }
        catch (RequestFailedException e) when (e.ErrorCode == BlobErrorCode.LeaseAlreadyPresent)
        {
            logger.LogDebug("Blob {BlobName} is already locked", blobClient.Name);

            return false;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to acquire lock for blob: {BlobName}", blobClient.Name);

            return false;
        }
    }

    public async Task ReleaseIfHeld()
    {
        if (!_isLocked || _leaseClient == null)
        {
            return;
        }

        try
        {
            await _leaseClient.ReleaseAsync();
            logger.LogDebug("Successfully released lease for blob: {BlobName}", blobClient.Name);
        }
        catch (RequestFailedException e)
            when (e.ErrorCode == BlobErrorCode.LeaseIdMismatchWithLeaseOperation
                || e.ErrorCode == BlobErrorCode.LeaseNotPresentWithLeaseOperation
            )
        {
            logger.LogDebug("Lease for blob {BlobName} was already released or expired", blobClient.Name);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to release lease for blob: {BlobName}", blobClient.Name);
        }
        finally
        {
            _isLocked = false;
            _leaseClient = null;
        }
    }
}
