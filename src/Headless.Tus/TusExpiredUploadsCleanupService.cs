// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using tusdotnet.FileLocks;
using tusdotnet.Interfaces;
using tusdotnet.Stores;

namespace Headless.Tus;

/// <summary>
/// Periodically removes expired <em>incomplete</em> uploads. Disk uploads are rechecked under the upload's
/// file lock. Other stores own the safety of <see cref="ITusExpirationStore.RemoveExpiredFilesAsync"/>.
/// </summary>
/// <remarks>
/// tusdotnet only sets the <c>Upload-Expires</c> header; nothing removes expired uploads unless
/// the application runs a job like this one. <c>AddHeadlessTus</c> registers it against the host's
/// <see cref="ITusStore"/>. It does nothing when <see cref="TusExpiredUploadsCleanupOptions.Enabled"/> is
/// <see langword="false"/> or the store does not implement <see cref="ITusExpirationStore"/>. Otherwise the
/// first pass runs immediately at startup (reclaiming
/// uploads that expired while the app was down, matching tusdotnet's sample cleanup service),
/// then every <see cref="TusExpiredUploadsCleanupOptions.Interval"/>; failures are logged and
/// the loop continues. When a cleanup gate is registered (by <c>UseDistributedLocks</c>),
/// a node runs a pass only when it claims it, so one node sweeps per interval.
/// </remarks>
internal sealed partial class TusExpiredUploadsCleanupService(
    ITusStore store,
    IOptions<TusExpiredUploadsCleanupOptions> options,
    TimeProvider timeProvider,
    ILogger<TusExpiredUploadsCleanupService> logger,
    ITusExpiredUploadsCleanupGate? gate = null,
    ITusFileLockProvider? fileLockProvider = null
) : BackgroundService
{
    private readonly ITusFileLockProvider _fileLockProvider = fileLockProvider ?? InMemoryFileLockProvider.Instance;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            LogCleanupDisabled(logger);

            return;
        }

        if (store is not ITusExpirationStore expirationStore)
        {
            LogStoreHasNoExpiration(logger, store.GetType().Name);

            return;
        }

        // Immediate first pass: reclaim uploads that expired while the app was down.
        await _RunPassAsync(expirationStore, stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(options.Value.Interval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await _RunPassAsync(expirationStore, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task _RunPassAsync(ITusExpirationStore expirationStore, CancellationToken stoppingToken)
    {
        try
        {
            // Hold the claim for most of the interval so the other nodes skip their ticks in it, but not all of it,
            // so this node's own next tick is not refused by its still-live claim.
            var interval = options.Value.Interval;

            if (
                gate is not null
                && !await gate.TryClaimPassAsync(interval - (interval / 10), stoppingToken).ConfigureAwait(false)
            )
            {
                LogCleanupPassSkipped(logger);
                return;
            }

            var removed = store is TusDiskStore diskStore
                ? await _RemoveExpiredDiskUploadsAsync(diskStore, stoppingToken).ConfigureAwait(false)
                : await expirationStore.RemoveExpiredFilesAsync(stoppingToken).ConfigureAwait(false);

            if (removed > 0)
            {
                LogRemovedExpiredUploads(logger, removed);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            LogCleanupFailed(logger, e);
        }
    }

    private async Task<int> _RemoveExpiredDiskUploadsAsync(TusDiskStore diskStore, CancellationToken cancellationToken)
    {
        var candidates = await diskStore.GetExpiredFilesAsync(cancellationToken).ConfigureAwait(false);
        var removed = 0;

        foreach (var fileId in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileLock = await _fileLockProvider.AquireLock(fileId).ConfigureAwait(false);

            if (!await fileLock.Lock().ConfigureAwait(false))
            {
                continue;
            }

            try
            {
                if (!await diskStore.FileExistAsync(fileId, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                // TusDiskStore selects candidates against the system clock, which also drives tusdotnet expiry.
                var expiresAt = await diskStore.GetExpirationAsync(fileId, cancellationToken).ConfigureAwait(false);

                if (expiresAt is null || expiresAt >= TimeProvider.System.GetUtcNow())
                {
                    continue;
                }

                var length = await diskStore.GetUploadLengthAsync(fileId, cancellationToken).ConfigureAwait(false);

                if (
                    length is not null
                    && length == await diskStore.GetUploadOffsetAsync(fileId, cancellationToken).ConfigureAwait(false)
                )
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await diskStore.DeleteFileAsync(fileId, cancellationToken).ConfigureAwait(false);
                removed++;
            }
            finally
            {
                await fileLock.ReleaseIfHeld().ConfigureAwait(false);
            }
        }

        return removed;
    }

    [LoggerMessage(
        EventId = 3260,
        Level = LogLevel.Information,
        Message = "Removed {Count} expired incomplete upload(s)"
    )]
    private static partial void LogRemovedExpiredUploads(ILogger logger, int count);

    [LoggerMessage(EventId = 3261, Level = LogLevel.Error, Message = "Expired-upload cleanup pass failed")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3266,
        Level = LogLevel.Debug,
        Message = "Skipped the expired-upload cleanup pass: another node claimed it"
    )]
    private static partial void LogCleanupPassSkipped(ILogger logger);

    [LoggerMessage(EventId = 3259, Level = LogLevel.Debug, Message = "Expired-upload cleanup is disabled on this host")]
    private static partial void LogCleanupDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 3269,
        Level = LogLevel.Information,
        Message = "Expired-upload cleanup has nothing to do: store {StoreType} does not support upload expiration"
    )]
    private static partial void LogStoreHasNoExpiration(ILogger logger, string storeType);
}

/// <summary>Options for the job that removes expired incomplete uploads.</summary>
[PublicAPI]
public sealed class TusExpiredUploadsCleanupOptions
{
    /// <summary>Whether this host runs the cleanup job.</summary>
    /// <remarks>
    /// Defaults to <see langword="true"/>. Turn it off on hosts that should not sweep the store, for example
    /// when a dedicated worker runs the job for the whole deployment.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>How often expired incomplete uploads are removed.</summary>
    /// <remarks>
    /// Defaults to 5 minutes. Each pass scans the store's uploads, so prefer a coarser interval
    /// for stores with many uploads. The expiration window itself is the upload endpoint's
    /// <see cref="TusUploadEndpointOptions.Expiration"/>, or <c>DefaultTusConfiguration.Expiration</c>
    /// on an endpoint mapped with tusdotnet's <c>MapTus</c>. In multi-node deployments every node runs its
    /// own loop against the same store. With <c>UseDistributedLocks</c> selected,
    /// the node that takes the cleanup lease runs the pass and holds the lease for 90% of the
    /// interval, so the store is swept about once per interval across the cluster. Without it,
    /// every node sweeps on every tick: deletions are idempotent so this is safe, but the scan
    /// load multiplies and the logged removal counts are per-node.
    /// </remarks>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
}

internal sealed class TusExpiredUploadsCleanupOptionsValidator : AbstractValidator<TusExpiredUploadsCleanupOptions>
{
    public TusExpiredUploadsCleanupOptionsValidator()
    {
        RuleFor(x => x.Interval).GreaterThan(TimeSpan.Zero);
    }
}
