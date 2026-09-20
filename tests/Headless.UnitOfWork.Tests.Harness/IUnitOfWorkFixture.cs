// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Tests;

/// <summary>
/// Provider fixture for the unit-of-work conformance scenarios. A fixture supplies isolated
/// <see cref="UnitOfWorkSession" />s; the harness drives every scenario through the session's
/// <see cref="IUnitOfWorkFactory" /> so the scenarios exercise exactly the public contract.
/// </summary>
public interface IUnitOfWorkFixture
{
    /// <summary>
    /// Creates an isolated unit-of-work environment: a fresh factory and a per-session capture of the
    /// factory's log output (so a scenario can assert on the leak warning and callback faults).
    /// </summary>
    UnitOfWorkSession CreateSession();
}

/// <summary>
/// One isolated unit-of-work environment handed to a scenario: the factory plus the captured logs.
/// </summary>
public sealed class UnitOfWorkSession(IUnitOfWorkFactory factory, IReadOnlyCollection<LogEntry> logs)
{
    /// <summary>The factory this scenario drives.</summary>
    public IUnitOfWorkFactory Factory { get; } = factory;

    /// <summary>Everything the factory (and its units) logged in this session, in emission order.</summary>
    public IReadOnlyCollection<LogEntry> Logs { get; } = logs;

    /// <summary>Begins a resource-less unit; convenience over <see cref="IUnitOfWorkFactory.BeginAsync(Headless.UnitOfWork.UnitOfWorkOptions?, System.Threading.CancellationToken)" />.</summary>
    public ValueTask<IUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        return Factory.BeginAsync(cancellationToken: cancellationToken);
    }
}
