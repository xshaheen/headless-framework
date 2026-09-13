// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination;
using Headless.CommitCoordination.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Npgsql;

/// <summary>
/// Enlists an already-open Npgsql transaction in commit coordination: pushes the ambient coordinated scope and
/// surfaces the live connection/transaction through <see cref="IRelationalCommitContext" /> so participants
/// (e.g. the outbox writer) can enlist post-commit work.
/// </summary>
/// <remarks>
/// Npgsql exposes no commit edge to observe, so the contract is <b>explicit</b>: the caller holds the returned
/// scope, commits or rolls back the transaction, and then signals the scope with the outcome. The enlist is
/// intentionally <b>synchronous</b> — an <c>AsyncLocal</c> push inside an <c>async</c> helper does not flow back
/// to the caller — so callers open the transaction (sync or async) and call this in their own frame before the
/// enlisting work:
/// <code>
/// await using var connection = new NpgsqlConnection(connectionString);
/// await connection.OpenAsync(ct);
/// await using var tx = await connection.BeginTransactionAsync(ct);
/// await using var scope = connection.EnlistCommitCoordination(tx, services);
/// // publish / save here — ICurrentCommitCoordinator.Current is now this scope
/// await tx.CommitAsync(ct);
/// await scope.SignalAsync(CommitOutcome.Committed); // explicit: the caller drives the signal
/// </code>
/// Disposing the scope without a signal discards the enlisted work; when the transaction had already completed
/// by then, a warning is logged because the signal was almost certainly forgotten. Prefer
/// <c>NpgsqlConnection.ExecuteCoordinatedTransactionAsync</c>, which signals for you.
/// </remarks>
[PublicAPI]
public static class HeadlessNpgsqlEnlistCommitCoordinationExtensions
{
    extension(NpgsqlConnection connection)
    {
        /// <summary>
        /// Pushes the ambient coordinated scope for an open Npgsql transaction. Signal the returned scope after
        /// committing or rolling back the transaction, then dispose it; an un-signalled dispose discards the
        /// enlisted work.
        /// </summary>
        /// <param name="transaction">The open Npgsql transaction to coordinate.</param>
        /// <param name="services">A service provider that resolves the commit coordination services.</param>
        /// <param name="cancellationToken">
        /// Observed only before the scope is pushed; a pre-cancelled token throws here rather than pushing an
        /// ambient scope. It does not govern the post-commit drain, which always runs to completion.
        /// </param>
        /// <returns>The coordinated scope; the caller owns it, signals it, and disposes it after the transaction completes.</returns>
        /// <exception cref="InvalidOperationException"><c>AddPostgreSqlCommitCoordination</c> was not called.</exception>
        public ICommitScope EnlistCommitCoordination(
            NpgsqlTransaction transaction,
            IServiceProvider services,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(transaction);
            Argument.IsNotNull(services);
            cancellationToken.ThrowIfCancellationRequested();

            var scopeFactory = services.GetRequiredService<ICommitScopeFactory>();
            var logger = (ILogger?)services.GetService<ILogger<PostgreSqlCommitScope>>() ?? NullLogger.Instance;

            return new PostgreSqlCommitScope(
                scopeFactory.Open(new RelationalCommitContext(() => connection, () => transaction)),
                () => _IsCompleted(transaction),
                logger
            );
        }
    }

    /// <summary>
    /// Npgsql keeps its completion flag internal and leaves <c>Connection</c> populated after commit, so the only
    /// public observable is the readiness guard on <see cref="NpgsqlTransaction.IsolationLevel" />, which throws
    /// once the transaction has committed, rolled back, or been disposed. Evaluated only on the un-signalled dispose
    /// path, so the exception cost never lands on a healthy commit; a driver that stops throwing degrades to "no
    /// warning", never to a false one.
    /// </summary>
    private static bool _IsCompleted(NpgsqlTransaction transaction)
    {
        try
        {
            _ = transaction.IsolationLevel;

            return false;
        }
        catch (InvalidOperationException)
        {
            // Completed ("no longer usable") or disposed (ObjectDisposedException derives from this type).
            return true;
        }
    }
}
