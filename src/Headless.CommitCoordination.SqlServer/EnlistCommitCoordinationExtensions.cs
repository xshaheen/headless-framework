// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination;
using Headless.CommitCoordination.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Data.SqlClient;

/// <summary>
/// Enlists an already-open raw-ADO SqlClient transaction in commit coordination: pushes the ambient coordinated
/// scope and surfaces the live connection/transaction through <see cref="IRelationalCommitContext" /> so
/// participants (e.g. the outbox writer) can enlist post-commit work.
/// </summary>
/// <remarks>
/// Raw SqlClient exposes no commit edge this package observes, so the contract is <b>explicit</b>: the caller
/// holds the returned scope, commits or rolls back the transaction, and then signals the scope with the outcome.
/// The enlist is intentionally <b>synchronous</b> — an <c>AsyncLocal</c> push inside an <c>async</c> helper does
/// not flow back to the caller — so callers open the transaction (sync or async) and call this in their own frame
/// before the enlisting work:
/// <code>
/// await using var connection = new SqlConnection(connectionString);
/// await connection.OpenAsync(ct);
/// await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
/// await using var scope = connection.EnlistCommitCoordination(tx, services);
/// // publish / save here — ICurrentCommitCoordinator.Current is now this scope
/// await tx.CommitAsync(ct);
/// await scope.SignalAsync(CommitOutcome.Committed); // explicit: the caller drives the signal
/// </code>
/// Disposing the scope without a signal discards the enlisted work; when the transaction had already completed
/// by then, a warning is logged because the signal was almost certainly forgotten. Prefer
/// <c>SqlConnection.ExecuteCoordinatedTransactionAsync</c>, which signals for you.
/// </remarks>
[PublicAPI]
public static class HeadlessSqlServerEnlistCommitCoordinationExtensions
{
    extension(SqlConnection connection)
    {
        /// <summary>
        /// Pushes the ambient coordinated scope for an open SqlClient transaction. Signal the returned scope after
        /// committing or rolling back the transaction, then dispose it; an un-signalled dispose discards the
        /// enlisted work.
        /// </summary>
        /// <param name="transaction">The open SqlClient transaction to coordinate.</param>
        /// <param name="services">A service provider that resolves the commit coordination services.</param>
        /// <param name="cancellationToken">
        /// Observed only before the scope is pushed; a pre-cancelled token throws here rather than pushing an
        /// ambient scope. It does not govern the post-commit drain, which always runs to completion.
        /// </param>
        /// <returns>The coordinated scope; the caller owns it, signals it, and disposes it after the transaction completes.</returns>
        /// <exception cref="InvalidOperationException"><c>AddSqlServerCommitCoordination</c> was not called.</exception>
        public ICommitScope EnlistCommitCoordination(
            SqlTransaction transaction,
            IServiceProvider services,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(transaction);
            Argument.IsNotNull(services);
            cancellationToken.ThrowIfCancellationRequested();

            var scopeFactory = services.GetRequiredService<ICommitScopeFactory>();
            var logger = (ILogger?)services.GetService<ILogger<SqlServerCommitScope>>() ?? NullLogger.Instance;

            return new SqlServerCommitScope(
                scopeFactory.Open(new RelationalCommitContext(() => connection, () => transaction)),
                // SqlClient detaches a transaction from its connection once it commits, rolls back, or is disposed.
                () => transaction.Connection is null,
                logger
            );
        }
    }
}
