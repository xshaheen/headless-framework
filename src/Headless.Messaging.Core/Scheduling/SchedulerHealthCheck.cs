// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Scheduling;

/// <summary>
/// Health check that verifies scheduler storage reachability and reports stale job status.
/// </summary>
internal sealed class SchedulerHealthCheck(
    IScheduledJobStorage storage,
    IOptions<SchedulerOptions> options,
    TimeProvider timeProvider
) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var threshold = timeProvider.GetUtcNow() - options.Value.StaleJobThreshold;
            var staleCount = await storage.GetStaleJobCountAsync(threshold, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            var data = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "stale_jobs", staleCount },
                { "latency_ms", sw.Elapsed.TotalMilliseconds },
            };

            if (staleCount > 0)
            {
                return HealthCheckResult.Degraded($"{staleCount} stale job(s) detected", data: data);
            }

            return HealthCheckResult.Healthy("Scheduler storage is reachable", data: data);
        }
        catch (Exception ex)
        {
            sw.Stop();

            return HealthCheckResult.Unhealthy(
                "Scheduler storage query failed",
                ex,
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "latency_ms", sw.Elapsed.TotalMilliseconds },
                }
            );
        }
    }
}
