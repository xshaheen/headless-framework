// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Headless.Tus.Locks;

/// <summary>
/// TUS file lock backed by an Azure Blob Storage lease.
/// </summary>
/// <remarks>
/// Acquires an exclusive Azure Blob lease on the upload blob to prevent concurrent PATCH requests
/// from corrupting block-level state. Returns <see langword="false"/> rather than throwing when
/// the blob is already leased or does not yet exist, matching tusdotnet's lock contract.
/// </remarks>
public sealed class AzureBlobFileLock(BlobClient blobClient, TimeSpan leaseDuration, ILogger<AzureBlobFileLock> logger)
    : ITusFileLock
{
    private BlobLeaseClient? _leaseClient;
    private bool _isLocked;

    /// <summary>
    /// Attempts to acquire an exclusive Azure Blob lease on the upload blob.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the lease was acquired (or was already held by this instance);
    /// <see langword="false"/> if the blob does not exist or another client holds the lease.
    /// </returns>
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
                logger.CannotLockMissingBlob(blobClient.Name);

                return false;
            }

            _leaseClient = blobClient.GetBlobLeaseClient();
            await _leaseClient.AcquireAsync(leaseDuration);

            _isLocked = true;
            logger.BlobLeaseAcquired(blobClient.Name);

            return true;
        }
        catch (RequestFailedException e) when (e.ErrorCode == BlobErrorCode.LeaseAlreadyPresent)
        {
            logger.BlobAlreadyLocked(blobClient.Name);

            return false;
        }
        catch (Exception e)
        {
            logger.BlobLockAcquireFailed(e, blobClient.Name);

            return false;
        }
    }

    /// <summary>
    /// Releases the Azure Blob lease if this instance currently holds it.
    /// </summary>
    /// <remarks>
    /// Silently no-ops when no lease is held. Azure lease-mismatch and lease-not-present errors
    /// are treated as already-released and are logged at <c>Debug</c> level rather than thrown.
    /// </remarks>
    public async Task ReleaseIfHeld()
    {
        if (!_isLocked || _leaseClient == null)
        {
            return;
        }

        try
        {
            await _leaseClient.ReleaseAsync();
            logger.BlobLeaseReleased(blobClient.Name);
        }
        catch (RequestFailedException e)
            when (e.ErrorCode == BlobErrorCode.LeaseIdMismatchWithLeaseOperation
                || e.ErrorCode == BlobErrorCode.LeaseNotPresentWithLeaseOperation
            )
        {
            logger.BlobLeaseAlreadyReleased(blobClient.Name);
        }
        catch (Exception e)
        {
            logger.BlobLeaseReleaseFailed(e, blobClient.Name);
        }
        finally
        {
            _isLocked = false;
            _leaseClient = null;
        }
    }
}

internal static partial class AzureBlobFileLockLog
{
    [LoggerMessage(EventId = 3217, Level = LogLevel.Warning, Message = "Cannot lock non-existent blob: {BlobName}")]
    public static partial void CannotLockMissingBlob(this ILogger logger, string blobName);

    [LoggerMessage(
        EventId = 3218,
        Level = LogLevel.Debug,
        Message = "Successfully acquired lease for blob: {BlobName}"
    )]
    public static partial void BlobLeaseAcquired(this ILogger logger, string blobName);

    [LoggerMessage(EventId = 3219, Level = LogLevel.Debug, Message = "Blob {BlobName} is already locked")]
    public static partial void BlobAlreadyLocked(this ILogger logger, string blobName);

    [LoggerMessage(EventId = 3220, Level = LogLevel.Error, Message = "Failed to acquire lock for blob: {BlobName}")]
    public static partial void BlobLockAcquireFailed(this ILogger logger, Exception e, string blobName);

    [LoggerMessage(
        EventId = 3221,
        Level = LogLevel.Debug,
        Message = "Successfully released lease for blob: {BlobName}"
    )]
    public static partial void BlobLeaseReleased(this ILogger logger, string blobName);

    [LoggerMessage(
        EventId = 3222,
        Level = LogLevel.Debug,
        Message = "Lease for blob {BlobName} was already released or expired"
    )]
    public static partial void BlobLeaseAlreadyReleased(this ILogger logger, string blobName);

    [LoggerMessage(EventId = 3223, Level = LogLevel.Error, Message = "Failed to release lease for blob: {BlobName}")]
    public static partial void BlobLeaseReleaseFailed(this ILogger logger, Exception e, string blobName);
}
