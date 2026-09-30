// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Fencing;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>Builds the Core lease services over a substituted store and a settable tenant.</summary>
internal sealed class FencingTestContext
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(30);

    public FencingTestContext()
    {
        var monitor = Substitute.For<IOptionsMonitor<FencingOptions>>();
        monitor.CurrentValue.Returns(_ => Options);

        Resolver = new LeaseRequestResolver(Tenant, monitor);
        Alerts = new LeaseTakeoverAlerts(monitor, Logger);
        Feature = new UnitOfWorkLeasesFeature(Resolver, Store, Alerts);
        Leases = new FencedLeases(Resolver, Store, Alerts);
    }

    public FencingOptions Options { get; } = new();

    public MutableCurrentTenant Tenant { get; } = new();

    public ILeaseStore Store { get; } = Substitute.For<ILeaseStore>();

    public LeaseRequestResolver Resolver { get; }

    public CapturingLogger<LeaseTakeoverAlerts> Logger { get; } = new();

    public LeaseTakeoverAlerts Alerts { get; }

    public UnitOfWorkLeasesFeature Feature { get; }

    public FencedLeases Leases { get; }

    /// <summary>An active unit over a live relational resource: every refusal gate passes.</summary>
    public static (IUnitOfWork Unit, IRelationalUnitOfWorkResource Resource) ActiveUnit(bool isOwned = true)
    {
        var resource = Substitute.For<IRelationalUnitOfWorkResource>();
        resource.IsOwned.Returns(isOwned);
        resource.IsTransactionCompleted.Returns(false);
        resource.Transaction.Returns(Substitute.For<DbTransaction>());

        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Active);
        unit.Resource.Returns(resource);

        return (unit, resource);
    }
}

internal sealed class MutableCurrentTenant : ICurrentTenant
{
    public bool IsAvailable => Id is not null;

    public string? Id { get; set; }

    public string? Name => null;

    public IDisposable Change(string? id, string? name = null)
    {
        var previous = Id;
        Id = id;

        return new Restore(this, previous);
    }

    private sealed class Restore(MutableCurrentTenant tenant, string? previous) : IDisposable
    {
        public void Dispose() => tenant.Id = previous;
    }
}

/// <summary>Records every entry logged through it, so a test can assert on the level, event, and message.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}
