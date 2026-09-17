// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Tests;

/// <summary>
/// Provider fixture for the unit-of-work conformance scenarios. A fixture supplies isolated
/// <see cref="UnitOfWorkSession" />s; the harness drives every scenario through the session's
/// <see cref="IUnitOfWorkManager" /> so the scenarios exercise exactly the public contract.
/// </summary>
public interface IUnitOfWorkFixture
{
    /// <summary>
    /// Creates an isolated unit-of-work environment: a fresh scoped manager and a per-session capture of the
    /// manager's log output (so a scenario can assert on the leak warning and callback faults).
    /// </summary>
    UnitOfWorkSession CreateSession();
}

/// <summary>
/// One isolated unit-of-work environment handed to a scenario: the scoped manager plus the captured logs.
/// Disposing the session disposes the manager (the service-scope edge).
/// </summary>
public sealed class UnitOfWorkSession(IUnitOfWorkManager manager, IReadOnlyCollection<LogEntry> logs) : IAsyncDisposable
{
    /// <summary>The scoped manager this scenario drives.</summary>
    public IUnitOfWorkManager Manager { get; } = manager;

    /// <summary>Everything the manager (and its units) logged in this session, in emission order.</summary>
    public IReadOnlyCollection<LogEntry> Logs { get; } = logs;

    /// <summary>Begins a resource-less unit; convenience over <see cref="IUnitOfWorkManager.BeginAsync(Headless.UnitOfWork.UnitOfWorkOptions?, System.Threading.CancellationToken)" />.</summary>
    public ValueTask<IUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        return Manager.BeginAsync(cancellationToken: cancellationToken);
    }

    /// <summary>Disposes the manager: the service scope ending with a possibly-leaked unit of work.</summary>
    public ValueTask DisposeAsync()
    {
        return ((IAsyncDisposable)Manager).DisposeAsync();
    }
}
