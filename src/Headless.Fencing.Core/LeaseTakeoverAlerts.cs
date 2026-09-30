// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Fencing;

/// <summary>
/// Logs a warning when a takeover grant or a sweep's abandonment brings a lease's takeover count to
/// <see cref="FencingOptions.TakeoverWarningThreshold" /> or above, so an executor that keeps losing its lease is
/// visible without reading the lease table.
/// </summary>
/// <remarks>
/// Only the events that raise the count report it, so one takeover logs once: a <see cref="LeaseGrantStatus.Granted" />
/// grant over an abandoned lease repeats the count the sweep already reported. An enlisted grant is reported when it
/// returns, before its unit commits, so a unit that rolls back can leave a warning for a takeover that never happened.
/// </remarks>
internal sealed partial class LeaseTakeoverAlerts(
    IOptionsMonitor<FencingOptions> options,
    ILogger<LeaseTakeoverAlerts>? logger = null
)
{
    private readonly ILogger _logger = logger ?? NullLogger<LeaseTakeoverAlerts>.Instance;

    /// <summary>Reports <paramref name="result" /> when it is a takeover at or above the threshold.</summary>
    public void OnGranted(LeaseGrantResult result)
    {
        if (result is { Status: LeaseGrantStatus.Takeover, Lease: { } lease } && _Reaches(result.TakeoverCount))
        {
            LogTakeoverThresholdReached(
                _logger,
                lease.Kind,
                lease.Resource,
                lease.TenantId,
                result.TakeoverCount,
                result.PreviousGeneration ?? 0,
                lease.Generation
            );
        }
    }

    /// <summary>Reports a sweep's abandonment of <paramref name="lease" /> when it is at or above the threshold.</summary>
    public void OnAbandoned(ExpiredLease lease)
    {
        if (_Reaches(lease.TakeoverCount))
        {
            LogAbandonThresholdReached(
                _logger,
                lease.Kind,
                lease.Resource,
                lease.TenantId,
                lease.TakeoverCount,
                lease.Generation
            );
        }
    }

    private bool _Reaches(int takeoverCount)
    {
        // Read on every call so a threshold changed through options reload applies to the next takeover.
        return options.CurrentValue.TakeoverWarningThreshold is { } threshold && takeoverCount >= threshold;
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "FencedLeaseTakeoverThresholdReached",
        Level = LogLevel.Warning,
        Message = "Fenced lease {Kind}/{Resource} (tenant {TenantId}) was taken over from an expired holder "
            + "{TakeoverCount} times since it last settled; generation {PreviousGeneration} lost it to {Generation}."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogTakeoverThresholdReached(
        ILogger logger,
        string kind,
        string resource,
        string? tenantId,
        int takeoverCount,
        long previousGeneration,
        long generation
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "FencedLeaseAbandonThresholdReached",
        Level = LogLevel.Warning,
        Message = "Fenced lease {Kind}/{Resource} (tenant {TenantId}) was abandoned by a sweep; it has lost "
            + "{TakeoverCount} expired holders since it last settled, the latest at generation {Generation}."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogAbandonThresholdReached(
        ILogger logger,
        string kind,
        string resource,
        string? tenantId,
        int takeoverCount,
        long generation
    );
}
