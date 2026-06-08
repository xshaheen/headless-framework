// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AmbientTransactions;

/// <summary>
/// Coordinates an ambient database transaction with deferred work that should run only after commit.
/// </summary>
[PublicAPI]
public interface IAmbientTransaction : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets or sets a value indicating whether the transaction should commit immediately after work is buffered.
    /// </summary>
    bool AutoCommit { get; set; }

    /// <summary>
    /// Gets or sets the underlying provider transaction.
    /// </summary>
    object? DbTransaction { get; set; }

    /// <summary>
    /// Registers work that should run when this ambient transaction is committed.
    /// </summary>
    void RegisterCommitWork(Func<CancellationToken, ValueTask> drain);

    /// <summary>
    /// Completes a transaction whose underlying provider transaction was committed externally.
    /// </summary>
    void CompleteExternally();

    /// <summary>
    /// Commits the underlying transaction and runs deferred commit work according to provider policy.
    /// </summary>
    void Commit();

    /// <summary>
    /// Commits the underlying transaction asynchronously and runs deferred commit work according to provider policy.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the underlying transaction and discards deferred commit work.
    /// </summary>
    void Rollback();

    /// <summary>
    /// Rolls back the underlying transaction asynchronously and discards deferred commit work.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
