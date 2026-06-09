// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// Wraps an EF <see cref="IDbContextTransaction" /> so its lifecycle drives commit coordination. Commit and
/// rollback delegate to the inner transaction (the registered <see cref="CommitCoordinationTransactionInterceptor" />
/// observes the edge and signals the coordinator); dispose runs an idempotent rollback safety-net so an
/// un-signalled dispose discards enlisted work instead of stranding the scope.
/// </summary>
internal sealed class CoordinatedDbContextTransaction(
    IDbContextTransaction inner,
    EntityFrameworkCommitSignalSource signalSource,
    DbTransaction providerTransactionKey
) : IDbContextTransaction
{
    public Guid TransactionId => inner.TransactionId;

    public bool SupportsSavepoints => inner.SupportsSavepoints;

    public void Commit() => inner.Commit();

    public Task CommitAsync(CancellationToken cancellationToken = default) => inner.CommitAsync(cancellationToken);

    public void Rollback() => inner.Rollback();

    public Task RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);

    public void CreateSavepoint(string name) => inner.CreateSavepoint(name);

    public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default) =>
        inner.CreateSavepointAsync(name, cancellationToken);

    public void RollbackToSavepoint(string name) => inner.RollbackToSavepoint(name);

    public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default) =>
        inner.RollbackToSavepointAsync(name, cancellationToken);

    public void ReleaseSavepoint(string name) => inner.ReleaseSavepoint(name);

    public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default) =>
        inner.ReleaseSavepointAsync(name, cancellationToken);

    public void Dispose()
    {
        // Idempotent safety-net: if commit/rollback already fired, the key is gone and this is a no-op; if the
        // caller disposed without signalling, this discards enlisted work (D8 — un-signalled dispose rolls back).
        // The drain is ConfigureAwait(false) throughout, so blocking here cannot deadlock under a sync context.
        signalSource.SignalRolledBackAsync(providerTransactionKey, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        inner.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await signalSource
            .SignalRolledBackAsync(providerTransactionKey, CancellationToken.None)
            .ConfigureAwait(false);
        await inner.DisposeAsync().ConfigureAwait(false);
    }
}
