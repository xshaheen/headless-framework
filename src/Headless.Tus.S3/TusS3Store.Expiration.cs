// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using Amazon.S3.Model;
using Headless.Tus.Internal;

namespace Headless.Tus;

public sealed partial class TusS3Store
{
    // A writer creates its multipart upload a moment before the state write that records it, so an unrecorded one is
    // only a leak once it is older than any append could take to record it.
    private static readonly TimeSpan _UnrecordedMultipartUploadGrace = TimeSpan.FromHours(1);

    /// <summary>
    /// Lists the incomplete uploads the cleanup pass would remove: those past their expiry, and those marked gone
    /// whatever their expiry.
    /// </summary>
    /// <param name="cancellationToken">token to cancel the listing</param>
    /// <returns>the file identifiers of the abandoned uploads found</returns>
    /// <remarks>
    /// Completed uploads are never listed and never read: they keep no state object under the working-set folder the
    /// pass lists, so the pass costs one listing per 1,000 in-progress uploads plus one read per upload, however many
    /// completed uploads the bucket holds. A completing upload counts once the expiry it had before completing has
    /// passed, unless its completion has already written the finished object and is recording it. A cancelled token
    /// returns the uploads found so far instead of throwing.
    /// </remarks>
    /// <exception cref="AmazonS3Exception">Storage refused the listing or a read.</exception>
    public async Task<IEnumerable<string>> GetExpiredFilesAsync(CancellationToken cancellationToken)
    {
        var scan = await _ScanInProgressUploadsAsync(cancellationToken).ConfigureAwait(false);

        return scan.Abandoned.ConvertAll(upload => upload.FileId);
    }

    /// <summary>
    /// Removes every abandoned incomplete upload with all its objects, and returns how many it removed.
    /// </summary>
    /// <param name="cancellationToken">token to cancel the listing</param>
    /// <returns>the number of uploads removed in this pass</returns>
    /// <remarks>
    /// <para>
    /// The pass runs outside tusdotnet's per-file lock, so it claims each upload before destroying it: it writes the
    /// state as gone with <c>If-Match</c> on the ETag it read. An upload that a request changed after the pass read it
    /// (an append that committed and moved the expiry) fails that write and is kept for the next pass. Once claimed, the
    /// upload's multipart upload is aborted, its finished object and record are deleted if present, and its working set
    /// is deleted with the state object last. A completing upload is destroyed as abandoned, never finished, so no
    /// finished object exists without its completion event. A completion moves the state on with its own
    /// <c>If-Match</c> write before it records the finished object, so the pass's claim and that write cannot both
    /// succeed: an upload whose completion got there first is skipped, and a completion that lost fails without
    /// recording anything.
    /// </para>
    /// <para>
    /// The pass also removes working-set folders whose state object is missing (leftovers of a completion or
    /// termination that stopped partway), and aborts multipart uploads older than an hour on the key of an upload it
    /// read when that upload's state does not record them. It never touches a multipart upload on any other key.
    /// </para>
    /// <para>
    /// Removal runs on <see cref="CancellationToken.None"/>: once an upload is found abandoned its removal finishes even
    /// when the caller shuts down, and the token bounds only the listing. One upload's failure is logged and does not
    /// stop the rest.
    /// </para>
    /// </remarks>
    /// <exception cref="AmazonS3Exception">Storage refused the listing or a read.</exception>
    public async Task<int> RemoveExpiredFilesAsync(CancellationToken cancellationToken)
    {
        var scan = await _ScanInProgressUploadsAsync(cancellationToken).ConfigureAwait(false);

        foreach (var fileId in scan.Orphans)
        {
            await _RemoveOrphanWorkingSetAsync(fileId).ConfigureAwait(false);
        }

        var removed = 0;

        foreach (var (fileId, upload) in scan.Abandoned)
        {
            if (await _RemoveAbandonedUploadAsync(fileId, upload).ConfigureAwait(false))
            {
                removed++;
            }
        }

        if (!scan.Cancelled)
        {
            await _AbortUnrecordedMultipartUploadsAsync(scan.Tracked, scan.Now, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogS3ExpiredUploadsRemoved(removed);

        return removed;
    }

    /// <summary>
    /// Lists the in-progress uploads one folder per upload, and reads each one's state once.
    /// </summary>
    private async Task<SweepScan> _ScanInProgressUploadsAsync(CancellationToken cancellationToken)
    {
        var scan = new SweepScan(_timeProvider.GetUtcNow());
        var root = _keys.WorkingSetRoot;
        var request = new ListObjectsV2Request
        {
            BucketName = _options.Bucket,
            Prefix = root,
            Delimiter = "/",
        };

        try
        {
            ListObjectsV2Response response;

            do
            {
                response = await _s3.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);

                foreach (var folder in response.CommonPrefixes ?? [])
                {
                    var fileId = folder[root.Length..^1];

                    if (TusS3Keys.IsValid(fileId))
                    {
                        await _ScanUploadAsync(scan, fileId, cancellationToken).ConfigureAwait(false);
                    }
                }

                request.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated is true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The cleanup job is shutting down; the uploads found so far are still handled, the next pass the rest.
            scan.Cancelled = true;
        }
        catch (Exception e)
        {
            _logger.LogS3ExpiredUploadsScanFailed(e);

            throw;
        }

        return scan;
    }

    private async Task _ScanUploadAsync(SweepScan scan, string fileId, CancellationToken cancellationToken)
    {
        TusS3LoadedUpload? upload;

        try
        {
            upload = await _stateStore.ReadIncompleteAsync(fileId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException e)
        {
            // A state this build cannot read (a newer schema, or a corrupt object) is skipped so it cannot stop the
            // pass; a newer schema is left to the build that wrote it.
            _logger.LogS3UnreadableStateSkipped(e, fileId);

            return;
        }

        if (upload?.State is not { } state)
        {
            scan.Orphans.Add(fileId);

            return;
        }

        scan.Tracked[fileId] = state.MultipartUploadId;

        if (_IsAbandoned(state, scan.Now))
        {
            scan.Abandoned.Add((fileId, upload));
        }
    }

    private bool _IsAbandoned(TusS3UploadState state, DateTimeOffset now)
    {
        return state.Status switch
        {
            TusS3UploadStatus.Gone => true,
            // The re-sent final PATCH stays the way a completing upload finishes, so the pass waits out the expiry it
            // had before completing.
            TusS3UploadStatus.Completing => state.ExpiresAtBeforeCompleting <= now,
            // The finished object exists and its completion is recording it; removing it now would delete an object
            // that completion reports as finished.
            TusS3UploadStatus.Committing => false,
            // An upload past the provider's abort window has lost its parts and already reads as missing.
            _ => state.ExpiresAt <= now || _MultipartUploadLifetimePassed(state),
        };
    }

    /// <summary>Claims an abandoned upload as gone, then destroys its objects.</summary>
    /// <returns><see langword="true"/> when the upload was claimed and removed</returns>
    private async Task<bool> _RemoveAbandonedUploadAsync(string fileId, TusS3LoadedUpload upload)
    {
        var state = upload.State!;
        state.Status = TusS3UploadStatus.Gone;

        try
        {
            await _stateStore.UpdateAsync(fileId, upload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (TusS3StateStore.IsStaleWrite(e))
        {
            _logger.LogS3ExpiredUploadChangedBeforeRemoval(fileId);

            return false;
        }
#pragma warning disable CA1031 // One upload's failure must not stop the pass; it is logged and retried next pass.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _logger.LogS3ExpiredUploadRemoveFailed(e, fileId);

            return false;
        }

        // Claimed: every later request on the upload now reads it as gone, so its objects can go.
        try
        {
            if (state.MultipartUploadId is { } uploadId)
            {
                await _AbortMultipartUploadIfExistsAsync(fileId, uploadId, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            var workingSet = await _ListWorkingSetAsync(fileId, CancellationToken.None).ConfigureAwait(false);
            await _DeleteUploadObjectsAsync(fileId, workingSet, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The upload is claimed as gone, so the next pass finds it again and finishes the removal.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _logger.LogS3ExpiredUploadRemoveFailed(e, fileId);

            return false;
        }

        return true;
    }

    /// <summary>Deletes a working-set folder whose state object is gone; it holds only leftover tail segments.</summary>
    private async Task _RemoveOrphanWorkingSetAsync(string fileId)
    {
        try
        {
            var keys = await _ListWorkingSetAsync(fileId, CancellationToken.None).ConfigureAwait(false);

            // A finished object or record under the id belongs to a completed upload and stays.
            await _DeleteObjectKeysAsync(keys, CancellationToken.None).ConfigureAwait(false);

            _logger.LogS3OrphanWorkingSetRemoved(fileId, keys.Count);
        }
#pragma warning disable CA1031 // Leftover segments only cost storage; the next pass tries again.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _logger.LogS3ExpiredUploadRemoveFailed(e, fileId);
        }
    }

    /// <summary>
    /// Aborts multipart uploads that a crash or a stale writer left on the key of an upload this pass read, when the
    /// upload's state does not record them and they are older than the grace period.
    /// </summary>
    private async Task _AbortUnrecordedMultipartUploadsAsync(
        Dictionary<string, string?> tracked,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (tracked.Count == 0)
        {
            return;
        }

        var prefix = _options.NormalizedPrefix;

        try
        {
            await foreach (
                var upload in TusS3Requests
                    .ListMultipartUploadsAsync(_s3, _options.Bucket, prefix, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                if (_IsUnrecordedLeak(upload, prefix, tracked, now, out var fileId))
                {
                    await _AbortMultipartUploadIfExistsAsync(fileId, upload.UploadId, CancellationToken.None)
                        .ConfigureAwait(false);

                    _logger.LogS3UnrecordedMultipartUploadAborted(fileId, upload.UploadId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down; the next pass finds the rest.
        }
#pragma warning disable CA1031 // A leaked multipart upload only costs storage; the removals this pass made still count.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _logger.LogS3UnrecordedMultipartUploadsScanFailed(e);
        }
    }

    private static bool _IsUnrecordedLeak(
        MultipartUpload upload,
        string prefix,
        Dictionary<string, string?> tracked,
        DateTimeOffset now,
        [NotNullWhen(true)] out string? fileId
    )
    {
        fileId = null;

        // Only the exact finished key of an upload this pass read belongs to the store; anything else under the prefix
        // may be another writer's.
        if (
            upload.Key is not { } key
            || !key.StartsWith(prefix, StringComparison.Ordinal)
            || !tracked.TryGetValue(key[prefix.Length..], out var recorded)
            || string.Equals(recorded, upload.UploadId, StringComparison.Ordinal)
            || upload.Initiated is not { } initiated
            || new DateTimeOffset(initiated.NormalizeToUtc()) > now - _UnrecordedMultipartUploadGrace
        )
        {
            return false;
        }

        fileId = key[prefix.Length..];

        return true;
    }

    /// <summary>What one listing pass found, in the order it found it.</summary>
    private sealed class SweepScan(DateTimeOffset now)
    {
        /// <summary>The instant every expiry in this pass is judged against.</summary>
        public DateTimeOffset Now { get; } = now;

        /// <summary>Uploads to remove, with the state and ETag the pass read.</summary>
        public List<(string FileId, TusS3LoadedUpload Upload)> Abandoned { get; } = [];

        /// <summary>Working-set folders with no state object.</summary>
        public List<string> Orphans { get; } = [];

        /// <summary>Every upload whose state the pass read, with the multipart upload that state records.</summary>
        public Dictionary<string, string?> Tracked { get; } = new(StringComparer.Ordinal);

        public bool Cancelled { get; set; }
    }
}
