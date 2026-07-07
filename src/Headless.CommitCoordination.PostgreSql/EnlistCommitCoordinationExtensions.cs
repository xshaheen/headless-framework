// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination;
using Headless.CommitCoordination.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Npgsql;

/// <summary>
/// Enlists an already-open Npgsql transaction in commit coordination: pushes the ambient coordinated scope and
/// surfaces the live connection/transaction through <see cref="IRelationalCommitContext" /> so participants
/// (e.g. the outbox writer) can enlist post-commit work.
/// </summary>
/// <remarks>
/// PostgreSQL has no diagnostic listener that surfaces the native transaction edge, so the model is
/// <b>inline (caller-driven)</b>: the caller holds the returned scope, commits or rolls back the Npgsql
/// transaction, and then signals the scope directly. As with every provider, the enlist is intentionally
/// <b>synchronous</b> — an <c>AsyncLocal</c> push inside an <c>async</c> helper does not flow back to the caller,
/// so callers open the transaction (sync or async) and call this in their own frame before the enlisting work:
/// <code>
/// await using var connection = new NpgsqlConnection(connectionString);
/// await connection.OpenAsync(ct);
/// await using var tx = await connection.BeginTransactionAsync(ct);
/// await using var scope = connection.EnlistCommitCoordination(tx, services);
/// // publish / save here — ICurrentCommitCoordinator.Current is now this scope
/// await tx.CommitAsync(ct);
/// await scope.SignalAsync(CommitOutcome.Committed); // inline: the caller drives the signal
/// </code>
/// If the caller never signals, disposing the scope discards the enlisted work (un-signalled dispose rolls back).
/// </remarks>
[PublicAPI]
public static class HeadlessNpgsqlEnlistCommitCoordinationExtensions
{
    extension(NpgsqlConnection connection)
    {
        /// <summary>
        /// Pushes the ambient coordinated scope for an open Npgsql transaction. Signal the returned scope after
        /// committing or rolling back the transaction, or dispose it to discard the enlisted work.
        /// </summary>
        /// <param name="transaction">The open Npgsql transaction to coordinate.</param>
        /// <param name="services">The scoped service provider captured for the post-commit drain.</param>
        /// <param name="cancellationToken">
        /// Observed only while attaching (before any work is enlisted); a pre-cancelled token throws here rather
        /// than pushing an ambient scope. It does not govern the post-commit drain (design decision D9).
        /// </param>
        /// <returns>The coordinated scope; signal or dispose it after the transaction completes.</returns>
        public ICommitScope EnlistCommitCoordination(
            NpgsqlTransaction transaction,
            IServiceProvider services,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(transaction);
            Argument.IsNotNull(services);

            var signalSource = services.GetRequiredService<PostgreSqlCommitSignalSource>();

            return signalSource.Attach(
                new CommitCoordinatorBindings
                {
                    Services = services,
                    Capabilities = [new RelationalCommitContext(() => connection, () => transaction)],
                    ProviderTransactionKey = transaction,
                },
                cancellationToken
            );
        }
    }
}
