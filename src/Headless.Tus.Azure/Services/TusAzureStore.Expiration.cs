// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using Headless.Tus.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Headless.Tus.Services;

public sealed partial class TusAzureStore : ITusExpirationStore
{
    /// <summary>
    /// Stores the expiration timestamp for the given upload in the blob's metadata.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="expires">the UTC instant after which the upload is considered expired</param>
    /// <param name="cancellationToken">unused; see remarks</param>
    /// <remarks>
    /// If the blob does not exist, the call is silently ignored and a warning is logged. The
    /// expiration value is persisted in the <c>tus_expiration</c> blob metadata key and evaluated
    /// during <c>GetExpiredFilesAsync</c> and <c>RemoveExpiredFilesAsync</c>.
    /// <para>
    /// <paramref name="cancellationToken"/> is deliberately ignored (mirroring
    /// <c>TusDiskStore</c>, whose store operations are non-cancellable): tusdotnet refreshes the
    /// sliding expiration <em>after</em> a PATCH using the request's token, which is already
    /// cancelled when the client paused or disconnected mid-request — exactly when the just
    /// -committed partial data still needs its expiration window extended so the client can resume.
    /// </para>
    /// </remarks>
    public async Task SetExpirationAsync(string fileId, DateTimeOffset expires, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var blobClient = _GetBlobClient(fileId);

        try
        {
            var azureFile = await _GetTusFileInfoAsync(blobClient, fileId, CancellationToken.None)
                .ConfigureAwait(false);

            if (azureFile == null)
            {
                _logger.CannotSetExpirationForMissingFile(fileId);

                return;
            }

            azureFile.Metadata.DateExpiration = expires;
            await _UpdateMetadataAsync(blobClient, azureFile, CancellationToken.None).ConfigureAwait(false);

            _logger.ExpirationSet(fileId, expires);
        }
        catch (Exception e)
        {
            _logger.ExpirationSetFailed(e, fileId);

            throw;
        }
    }

    /// <summary>
    /// Returns the expiration timestamp stored for the given upload, or <see langword="null"/>
    /// if the file does not exist, no expiration has been set, or the upload is complete.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>the expiration instant, or <see langword="null"/></returns>
    /// <remarks>
    /// Completed uploads report no expiration — a deliberate divergence from
    /// <c>TusDiskStore</c>, which keeps returning the stored timestamp. The TUS Expiration
    /// extension covers <em>unfinished</em> uploads, but tusdotnet refreshes the sliding
    /// expiration on the PATCH that completes an upload and its <c>FileHasNotExpired</c>
    /// requirement 404s any upload whose expiration has passed — so reporting the stale
    /// timestamp would make completed uploads unreachable through tus (no HEAD, and no
    /// DELETE/termination) once the window elapses, even though this store never reaps them.
    /// </remarks>
    public async Task<DateTimeOffset?> GetExpirationAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var azureFile = await _GetTusFileInfoAsync(fileId, cancellationToken).ConfigureAwait(false);

        if (azureFile is null)
        {
            return null;
        }

        // Completed uploads report no expiration (see remarks); reuse the shared incompleteness predicate
        // so the HEAD-expiration report and the reaper cannot disagree on whether an upload is reapable.
        if (!_IsIncompleteUpload(azureFile.Metadata.UploadLength, azureFile.CurrentContentLength))
        {
            return null;
        }

        return azureFile.Metadata.DateExpiration;
    }

    /// <summary>
    /// Enumerates the <em>incomplete</em> uploads in the configured container whose expiration
    /// timestamp is in the past.
    /// </summary>
    /// <param name="cancellationToken">token to cancel the enumeration</param>
    /// <returns>
    /// a collection of file identifiers for unfinished uploads whose <c>tus_expiration</c>
    /// metadata value is at or before the current UTC time
    /// </returns>
    /// <remarks>
    /// The TUS Expiration extension covers <em>unfinished</em> uploads only, and tusdotnet keeps
    /// refreshing the (sliding) expiration on the PATCH that completes an upload — so completed
    /// uploads routinely carry a past expiration. They are excluded here (matching
    /// <c>TusDiskStore</c>) so cleanup can never destroy data the application has not consumed
    /// yet. An upload with no declared length (Creation-Defer-Length in progress) counts as
    /// incomplete. A cancelled <paramref name="cancellationToken"/> returns the files found so
    /// far instead of throwing (<c>TusDiskStore</c> parity: the caller is typically a cleanup job
    /// shutting down, and the next pass picks up the remainder).
    /// </remarks>
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

                if (!(metadata.DateExpiration <= now))
                {
                    continue;
                }

                // ContentLength is the committed length (staged-but-unverified checksum blocks are
                // excluded), i.e. the same offset GetUploadOffsetAsync reports to resuming clients.
                var committedLength = blobItem.Properties.ContentLength ?? 0;
                var isIncomplete = _IsIncompleteUpload(metadata.UploadLength, committedLength);

                if (!isIncomplete)
                {
                    continue;
                }

                var fileId = _ExtractFileIdFromBlobName(blobItem.Name);

                if (!string.IsNullOrEmpty(fileId))
                {
                    expiredFiles.Add(fileId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cleanup-job shutdown: return what was found so far rather than throwing.
        }
        catch (Exception e)
        {
            _logger.GetExpiredFilesFailed(e);

            throw;
        }

        return expiredFiles;
    }

    /// <summary>
    /// Deletes all expired <em>incomplete</em> uploads discovered by <c>GetExpiredFilesAsync</c>
    /// and returns the number of blobs successfully removed. Completed uploads are never removed,
    /// even when their expiration timestamp has passed.
    /// </summary>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>the number of expired files deleted in this call</returns>
    /// <remarks>
    /// Individual deletion failures are logged at <c>Error</c> level and do not abort the
    /// remaining deletions. The method always returns the count of files that were actually
    /// removed rather than throwing on partial failure. Deletions run on
    /// <c>CancellationToken.None</c> (<c>TusDiskStore</c> parity, and the store's mutations-are-
    /// must-complete policy): once a file is enumerated as expired, its delete finishes even when
    /// the caller's token cancels mid-pass — otherwise a shutdown would abandon the remainder
    /// with spurious per-file errors. The token still bounds the enumeration itself.
    /// </remarks>
    public async Task<int> RemoveExpiredFilesAsync(CancellationToken cancellationToken)
    {
        var expiredFiles = await GetExpiredFilesAsync(cancellationToken).ConfigureAwait(false);
        var removedCount = 0;

        foreach (var fileId in expiredFiles)
        {
            try
            {
                await DeleteFileAsync(fileId, CancellationToken.None).ConfigureAwait(false);
                removedCount++;
            }
            catch (Exception e)
            {
                _logger.RemoveExpiredFileFailed(e, fileId);
            }
        }

        _logger.ExpiredFilesRemoved(removedCount);

        return removedCount;
    }

    // Single source of truth for "is this upload still incomplete?". Used by GetExpirationAsync (which
    // reports no expiration for completed uploads) and GetExpiredFilesAsync / RemoveExpiredFilesAsync
    // (which reap only incomplete uploads). A null upload length (Creation-Defer-Length in progress)
    // counts as incomplete. Keeping one predicate stops the two paths from drifting via a hand-rolled
    // De Morgan inverse and disagreeing on whether an upload is reapable.
    private static bool _IsIncompleteUpload(long? uploadLength, long currentContentLength)
    {
        return uploadLength is null || currentContentLength < uploadLength.Value;
    }
}

internal static partial class TusAzureStoreExpirationLog
{
    [LoggerMessage(
        EventId = 3206,
        Level = LogLevel.Warning,
        Message = "Cannot set expiration for non-existent file {FileId}"
    )]
    public static partial void CannotSetExpirationForMissingFile(this ILogger logger, string fileId);

    [LoggerMessage(
        EventId = 3207,
        Level = LogLevel.Debug,
        Message = "Set expiration for file {FileId} to {ExpirationDate}"
    )]
    public static partial void ExpirationSet(this ILogger logger, string fileId, DateTimeOffset expirationDate);

    [LoggerMessage(EventId = 3208, Level = LogLevel.Error, Message = "Failed to set expiration for file {FileId}")]
    public static partial void ExpirationSetFailed(this ILogger logger, Exception e, string fileId);

    [LoggerMessage(EventId = 3209, Level = LogLevel.Error, Message = "Failed to get expired files")]
    public static partial void GetExpiredFilesFailed(this ILogger logger, Exception e);

    [LoggerMessage(EventId = 3210, Level = LogLevel.Error, Message = "Failed to remove expired file {FileId}")]
    public static partial void RemoveExpiredFileFailed(this ILogger logger, Exception e, string fileId);

    [LoggerMessage(EventId = 3211, Level = LogLevel.Information, Message = "Removed {RemovedCount} expired files")]
    public static partial void ExpiredFilesRemoved(this ILogger logger, int removedCount);
}
