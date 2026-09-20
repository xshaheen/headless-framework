// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Provider fixture for the resource-backed unit-of-work conformance scenarios: a real relational transaction
/// (PostgreSQL, SQL Server) coordinated through <see cref="IUnitOfWorkFactory" />, instead of the in-memory
/// <see cref="FakeUnitOfWorkResource" />. Every scenario proves its outcome through <see cref="CountProbeRowsAsync" />
/// on an independent connection — never through factory state alone.
/// </summary>
public interface IUnitOfWorkResourceFixture
{
    /// <summary>Creates an isolated session: a fresh factory, optionally capturing its log output.</summary>
    UnitOfWorkResourceSession CreateSession(CapturingLoggerProvider? logs = null);

    /// <summary>
    /// Begins an owned unit of work on a fresh connection: the transaction starts on this line and
    /// <see cref="IUnitOfWork.CompleteAsync" /> commits it.
    /// </summary>
    ValueTask<UnitOfWorkResourceHandle> BeginOwnedAsync(
        IUnitOfWorkFactory factory,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Opens a connection and transaction and enlists it in observed mode: the caller drives commit/rollback
    /// through the returned handle, then calls <see cref="IUnitOfWork.CompleteAsync" /> or
    /// <see cref="IUnitOfWork.RollbackAsync" /> to settle the unit.
    /// </summary>
    Task<UnitOfWorkObservedHandle> EnlistObservedAsync(IUnitOfWorkFactory factory, CancellationToken cancellationToken);

    /// <summary>Inserts one probe row inside <paramref name="unitOfWork" />'s live transaction.</summary>
    Task InsertProbeRowAsync(IUnitOfWork unitOfWork, string name, CancellationToken cancellationToken);

    /// <summary>Counts probe rows using a connection independent of any unit's transaction.</summary>
    Task<int> CountProbeRowsAsync(CancellationToken cancellationToken);

    /// <summary>Creates the probe table when absent and deletes all probe rows between scenarios.</summary>
    Task ResetAsync(CancellationToken cancellationToken);
}

/// <summary>One isolated resource-conformance session: the scoped provider, its DI scope, and the manager it hosts.</summary>
public sealed class UnitOfWorkResourceSession(
    ServiceProvider provider,
    AsyncServiceScope scope,
    IUnitOfWorkFactory factory
) : IAsyncDisposable
{
    /// <summary>The factory this scenario drives.</summary>
    public IUnitOfWorkFactory Factory { get; } = factory;

    /// <summary>Disposes the DI scope (draining a still-active unit as a leak) and then the provider.</summary>
    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync().ConfigureAwait(false);
        await provider.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// An owned unit of work plus the connection backing it. Disposing completes the unit's own disposal contract
/// (a no-op once it already reached a terminal state) and then releases the connection.
/// </summary>
public sealed class UnitOfWorkResourceHandle(IUnitOfWork unitOfWork, DbConnection connection) : IAsyncDisposable
{
    /// <summary>The begun unit; the scenario completes or rolls it back explicitly.</summary>
    public IUnitOfWork UnitOfWork { get; } = unitOfWork;

    public async ValueTask DisposeAsync()
    {
        await UnitOfWork.DisposeAsync().ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// An observed unit of work plus the caller-owned connection and transaction backing it. The scenario commits or
/// rolls the transaction back itself through this handle before settling the unit.
/// </summary>
public sealed class UnitOfWorkObservedHandle(IUnitOfWork unitOfWork, DbConnection connection, DbTransaction transaction)
    : IAsyncDisposable
{
    /// <summary>The enlisted unit; the scenario completes or rolls it back explicitly.</summary>
    public IUnitOfWork UnitOfWork { get; } = unitOfWork;

    /// <summary>Commits the caller-owned transaction (the edge the unit's <c>CompleteAsync</c> observes).</summary>
    public Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        return transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Rolls the caller-owned transaction back (the edge the unit's <c>RollbackAsync</c> observes).</summary>
    public Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        return transaction.RollbackAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await UnitOfWork.DisposeAsync().ConfigureAwait(false);
        await transaction.DisposeAsync().ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
    }
}
