// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Idempotency;

/// <summary>
/// Deletes idempotency records past their retention on a fixed interval, then the fenced leases those records left
/// behind. Disabled when <see cref="IdempotentOperationsOptions.PurgeInterval" /> is <see langword="null" />.
/// </summary>
/// <remarks>
/// Records are purged before leases so a lease is never deleted while a record that may still be completed under it
/// survives; the lease purge then removes terminal idempotency leases older than the default retention. The record
/// store owns only its own table, so the lease half goes through the fencing API. A failed run is logged and retried
/// at the next interval rather than stopping the host.
/// </remarks>
internal sealed partial class IdempotencyRetentionService(
    IIdempotencyRecordStore store,
    IFencedLeases leases,
    IOptionsMonitor<IdempotentOperationsOptions> options,
    TimeProvider timeProvider,
    ILogger<IdempotencyRetentionService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Read on every round, so an interval changed through options reload applies to the next one.
            var interval = options.CurrentValue.PurgeInterval;

            if (interval is null)
            {
                return;
            }

            try
            {
                // Waits first: at startup the provider's storage initializer may not have created the table yet.
                await Task.Delay(interval.Value, timeProvider, stoppingToken).ConfigureAwait(false);
                await _PurgeAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A hosted-service loop boundary: a failed purge is logged and retried next interval instead of stopping the host.
            catch (Exception e)
#pragma warning restore CA1031
            {
                LogPurgeFailed(logger, e);
            }
        }
    }

    private async Task _PurgeAsync(CancellationToken cancellationToken)
    {
        var current = options.CurrentValue;
        var records = 0;
        int deleted;

        // Bounded batches keep each delete statement's locks short; a short batch means nothing past retention is left.
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            deleted = await store
                .PurgeAsync(TimeSpan.Zero, current.PurgeBatchSize, cancellationToken)
                .ConfigureAwait(false);
            records += deleted;
        } while (deleted >= current.PurgeBatchSize);

        var purgedLeases = await leases
            .PurgeAsync(IdempotentAdmission.LeaseKind, current.DefaultRetention, cancellationToken)
            .ConfigureAwait(false);

        LogPurged(logger, records, purgedLeases);
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "IdempotencyPurged",
        Level = LogLevel.Debug,
        Message = "Idempotency retention purge deleted {RecordCount} records and {LeaseCount} leases"
    )]
    private static partial void LogPurged(ILogger logger, int recordCount, int leaseCount);

    [LoggerMessage(
        EventId = 2,
        EventName = "IdempotencyPurgeFailed",
        Level = LogLevel.Error,
        Message = "Idempotency retention purge failed; retrying at the next interval"
    )]
    private static partial void LogPurgeFailed(ILogger logger, Exception exception);
}
