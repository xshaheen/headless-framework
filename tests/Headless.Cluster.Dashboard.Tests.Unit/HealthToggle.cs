// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tests;

/// <summary>A health check whose status a test sets, and that counts how often it ran.</summary>
internal sealed class HealthToggle
{
    private int _runs;

    public HealthStatus Status { get; set; } = HealthStatus.Healthy;

    public int Runs => Volatile.Read(ref _runs);

    /// <summary>Registers this toggle as the check <paramref name="name"/>.</summary>
    public IHealthChecksBuilder AddTo(IHealthChecksBuilder builder, string name)
    {
        return builder.AddCheck(
            name,
            () =>
            {
                Interlocked.Increment(ref _runs);

                return new HealthCheckResult(Status, $"{name} reports {Status}");
            },
            tags: ["ready"]
        );
    }
}

/// <summary>Records every change the module publishes to this node's streams.</summary>
internal sealed class RecordingPublisher : IDashboardEventPublisher
{
    private readonly Lock _lock = new();
    private readonly List<DashboardChange> _changes = [];

    public IReadOnlyList<DashboardChange> Changes
    {
        get
        {
            lock (_lock)
            {
                return [.. _changes];
            }
        }
    }

    public void Publish(DashboardChange change)
    {
        lock (_lock)
        {
            _changes.Add(change);
        }
    }
}
