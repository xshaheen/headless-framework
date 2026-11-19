// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using Amazon.S3.Model;
using Headless.MultiTenancy;
using Headless.Tus.Internal;

namespace Headless.Tus;

public sealed partial class TusS3Store
{
    // A writer creates its multipart upload a moment before the state write that records it, so an unrecorded one is
    // only a leak once it is older than any append could take to record it.
    private static readonly TimeSpan _UnrecordedMultipartUploadGrace = TimeSpan.FromHours(1);

    // A completion records its finished object within seconds of marking the upload committing, since every storage
    // call it makes is bounded by the SDK's timeouts. A committing upload older than this lost its completion to a
    // crash, and the client has not re-sent its final PATCH. The one case this misjudges is a completion frozen for
    // longer, between two calls, which would then record an object the sweep already deleted.
    private static readonly TimeSpan _AbandonedCommitGrace = TimeSpan.FromHours(1);

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
    /// <para>
    /// The listing covers the uploads kept outside any tenant and every tenant's uploads, whatever tenant is ambient,
    /// so the identifiers of different tenants come back in one sequence.
    /// </para>
    /// </remarks>
    /// <exception cref="AmazonS3Exception">Storage refused the listing or a read.</exception>
    public async Task<IEnumerable<string>> GetExpiredFilesAsync(CancellationToken cancellationToken)
    {
        List<string> expired = [];

        await _ForEachScopeAsync(
                scan =>
                {
                    expired.AddRange(scan.Abandoned.Select(upload => upload.FileId));

                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return expired;
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
    /// <para>
    /// The pass never reads the ambient tenant. It handles the uploads kept outside any tenant first and then, one
    /// tenant at a time, the uploads of every tenant that has one in progress, so the cleanup job covers them all
    /// from outside any tenant. Finding the tenants costs one more listing, sent only when a tenant's upload is in
    /// progress.
    /// </para>
    /// </remarks>
    /// <exception cref="AmazonS3Exception">Storage refused the listing or a read.</exception>
    public async Task<int> RemoveExpiredFilesAsync(CancellationToken cancellationToken)
    {
        var removed = 0;

        await _ForEachScopeAsync(
                async scan =>
                    removed += await _RemoveAbandonedUploadsAsync(scan, cancellationToken).ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        _logger.LogS3ExpiredUploadsRemoved(removed);

        return removed;
    }

    /// <summary>
    /// Scans the in-progress uploads kept outside any tenant, then those of each tenant that has one, and hands
    /// each scan to <paramref name="handle"/> before the next one starts, so the pass holds one tenant's uploads
    /// at a time.
    /// </summary>
    private async Task _ForEachScopeAsync(Func<SweepScan, Task> handle, CancellationToken cancellationToken)
    {
        var scan = await _ScanInProgressUploadsAsync(_unscopedKeys, cancellationToken).ConfigureAwait(false);
        await handle(scan).ConfigureAwait(false);

        // Tenants are looked up only when the listing above showed their folder, so a bucket that holds no tenant's
        // upload in progress costs the pass nothing more than before.
        if (!scan.HasTenantFolder || scan.Cancelled)
        {
            return;
        }

        foreach (var tenant in await _ListTenantsInProgressAsync(cancellationToken).ConfigureAwait(false))
        {
            scan = await _ScanInProgressUploadsAsync(_unscopedKeys.ForTenant(tenant), cancellationToken)
                .ConfigureAwait(false);
            await handle(scan).ConfigureAwait(false);

            if (scan.Cancelled)
            {
                return;
            }
        }
    }

    /// <summary>
    /// The tenants that have a working set, one listing page per 1,000 of them. Only the working-set folder is
    /// listed, so the cost follows the tenants with an upload in progress, not every tenant that ever uploaded.
    /// </summary>
    private async Task<List<string>> _ListTenantsInProgressAsync(CancellationToken cancellationToken)
    {
        List<string> tenants = [];
        var root = _unscopedKeys.TenantWorkingSetsRoot;
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
                    var tenant = folder[root.Length..^1];

                    // The store writes under a tenant only after this same check, so any other folder is not its own.
                    if (TenantStorageSegment.IsValid(tenant))
                    {
                        tenants.Add(tenant);
                    }
                }

                request.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated is true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The cleanup job is shutting down; the tenants found so far are still handled, the next pass the rest.
        }
        catch (Exception e)
        {
            _logger.LogS3ExpiredUploadsScanFailed(e);

            throw;
        }

        return tenants;
    }

    /// <summary>Removes what one scan found: its orphan working sets, abandoned uploads, and leaked multipart uploads.</summary>
    /// <returns>the number of uploads removed</returns>
    private async Task<int> _RemoveAbandonedUploadsAsync(SweepScan scan, CancellationToken cancellationToken)
    {
        foreach (var fileId in scan.Orphans)
        {
            await _RemoveOrphanWorkingSetAsync(scan.Keys, fileId).ConfigureAwait(false);
        }

        var removed = 0;

        foreach (var (fileId, upload) in scan.Abandoned)
        {
            if (await _RemoveAbandonedUploadAsync(scan.Keys, fileId, upload).ConfigureAwait(false))
            {
                removed++;
            }
        }

        if (!scan.Cancelled)
        {
            await _AbortUnrecordedMultipartUploadsAsync(scan.Keys, scan.Tracked, scan.Now, cancellationToken)
                .ConfigureAwait(false);
        }

        return removed;
    }

    /// <summary>
    /// Lists the in-progress uploads <paramref name="keys"/> covers (one tenant's, or the ones kept outside any
    /// tenant), one folder per upload, and reads each one's state once.
    /// </summary>
    private async Task<SweepScan> _ScanInProgressUploadsAsync(TusS3Keys keys, CancellationToken cancellationToken)
    {
        var scan = new SweepScan(keys, _timeProvider.GetUtcNow());
        var root = keys.WorkingSetRoot;
        var tenantsRoot = keys.TenantWorkingSetsRoot;
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

                    // The folder that holds the tenants sits beside the working sets kept outside any tenant. Its
                    // name is no file id, so it is never read as an upload; each tenant in it gets a scan of its own.
                    if (TusS3Keys.IsValid(fileId))
                    {
                        await _ScanUploadAsync(keys, scan, fileId, cancellationToken).ConfigureAwait(false);
                    }
                    else if (string.Equals(folder, tenantsRoot, StringComparison.Ordinal))
                    {
                        scan.HasTenantFolder = true;
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

    private async Task _ScanUploadAsync(
        TusS3Keys keys,
        SweepScan scan,
        string fileId,
        CancellationToken cancellationToken
    )
    {
        TusS3LoadedUpload? upload;

        try
        {
            upload = await _stateStore.ReadIncompleteAsync(keys, fileId, cancellationToken).ConfigureAwait(false);
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
            // The finished object exists. A client may still re-send the final PATCH to finish it until the expiry it
            // had before completing, and a completion may still be recording it until the grace period has passed.
            TusS3UploadStatus.Committing => state.ExpiresAtBeforeCompleting <= now
                && state.CommittingAt <= now - _AbandonedCommitGrace,
            // An upload past the provider's abort window has lost its parts and already reads as missing.
            _ => state.ExpiresAt <= now || _MultipartUploadLifetimePassed(state),
        };
    }

    /// <summary>Claims an abandoned upload as gone, then destroys its objects.</summary>
    /// <returns><see langword="true"/> when the upload was claimed and removed</returns>
    private async Task<bool> _RemoveAbandonedUploadAsync(TusS3Keys keys, string fileId, TusS3LoadedUpload upload)
    {
        var state = upload.State!;
        state.Status = TusS3UploadStatus.Gone;

        try
        {
            await _stateStore.UpdateAsync(keys, fileId, upload, CancellationToken.None).ConfigureAwait(false);
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
                await _AbortMultipartUploadIfExistsAsync(keys, fileId, uploadId, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            var workingSet = await _ListWorkingSetAsync(keys, fileId, CancellationToken.None).ConfigureAwait(false);
            await _DeleteUploadObjectsAsync(keys, fileId, workingSet, CancellationToken.None).ConfigureAwait(false);
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
    private async Task _RemoveOrphanWorkingSetAsync(TusS3Keys keys, string fileId)
    {
        try
        {
            var objectKeys = await _ListWorkingSetAsync(keys, fileId, CancellationToken.None).ConfigureAwait(false);

            // A finished object or record under the id belongs to a completed upload and stays.
            await _DeleteObjectKeysAsync(objectKeys, CancellationToken.None).ConfigureAwait(false);

            _logger.LogS3OrphanWorkingSetRemoved(fileId, objectKeys.Count);
        }
#pragma warning disable CA1031 // Leftover segments only cost storage; the next pass tries again.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _logger.LogS3ExpiredUploadRemoveFailed(e, fileId);
        }
    }

    /// <summary>
    /// Aborts multipart uploads that a crash or a stale writer left on the key of an upload this scan read, when the
    /// upload's state does not record them and they are older than the grace period.
    /// </summary>
    /// <remarks>
    /// The listing for the uploads kept outside any tenant also returns every tenant's multipart uploads, because
    /// their keys start with the same prefix. None of them is taken for a leak: a tenant's key continues with the
    /// tenants folder and a slash, which no file id this scan read can equal.
    /// </remarks>
    private async Task _AbortUnrecordedMultipartUploadsAsync(
        TusS3Keys keys,
        Dictionary<string, string?> tracked,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (tracked.Count == 0)
        {
            return;
        }

        var prefix = keys.FinishedObjectRoot;

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
                    await _AbortMultipartUploadIfExistsAsync(keys, fileId, upload.UploadId, CancellationToken.None)
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

        // Only the exact finished key of an upload this scan read is judged; anything else under the prefix may be
        // another writer's, or an upload of a tenant this scan does not cover.
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

    /// <summary>
    /// What one listing found among one tenant's uploads, or among the ones kept outside any tenant, in the order it
    /// found it.
    /// </summary>
    private sealed class SweepScan(TusS3Keys keys, DateTimeOffset now)
    {
        /// <summary>The keys of the uploads this scan covers.</summary>
        public TusS3Keys Keys { get; } = keys;

        /// <summary>The instant every expiry in this pass is judged against.</summary>
        public DateTimeOffset Now { get; } = now;

        /// <summary>Uploads to remove, with the state and ETag the pass read.</summary>
        public List<(string FileId, TusS3LoadedUpload Upload)> Abandoned { get; } = [];

        /// <summary>Working-set folders with no state object.</summary>
        public List<string> Orphans { get; } = [];

        /// <summary>Every upload whose state the pass read, with the multipart upload that state records.</summary>
        public Dictionary<string, string?> Tracked { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Whether the listing showed the folder that holds each tenant's working sets, which only the scan of the
        /// uploads kept outside any tenant can see.
        /// </summary>
        public bool HasTenantFolder { get; set; }

        public bool Cancelled { get; set; }
    }
}
