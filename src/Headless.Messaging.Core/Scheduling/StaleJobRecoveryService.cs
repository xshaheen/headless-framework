// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Scheduling;

/// <summary>
/// Background service that polls for stale jobs and releases them back to Pending status.
/// </summary>
internal sealed class StaleJobRecoveryService(
    IScheduledJobStorage storage,
    ILogger<StaleJobRecoveryService> logger,
    IOptions<SchedulerOptions> options
) : BackgroundService
{
    private readonly SchedulerOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var released = await storage
                    .ReleaseStaleJobsAsync(_options.StaleJobThreshold, stoppingToken)
                    .ConfigureAwait(false);

                if (released > 0)
                {
                    logger.LogWarning(
                        "Released {Count} stale jobs (threshold: {Threshold})",
                        released,
                        _options.StaleJobThreshold
                    );
                }

                // Mark orphaned Running executions as TimedOut after releasing stale jobs
                var timedOut = await storage.TimeoutStaleExecutionsAsync(stoppingToken).ConfigureAwait(false);

                if (timedOut > 0)
                {
                    logger.LogWarning("Timed out {Count} orphaned execution records", timedOut);
                }

                // Purge old completed execution records to prevent unbounded growth
                var purged = await storage
                    .PurgeExecutionsAsync(_options.ExecutionRetention, stoppingToken)
                    .ConfigureAwait(false);

                if (purged > 0)
                {
                    logger.LogInformation("Purged {Count} old execution records", purged);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Stale job recovery loop must not crash on transient errors
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Stale job recovery error");
            }

            await Task.Delay(_options.StaleJobCheckInterval, stoppingToken).ConfigureAwait(false);
        }
    }
}
