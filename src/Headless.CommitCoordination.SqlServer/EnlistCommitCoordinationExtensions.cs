// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Enlists an already-open raw-ADO SqlClient transaction in commit coordination: pushes the ambient coordinated
/// scope and surfaces the live connection/transaction through <see cref="IRelationalCommitContext" /> so participants
/// (e.g. the outbox writer) can enlist post-commit work. The registered out-of-band
/// <see cref="SqlServerCommitDiagnosticObserver" /> detects the native commit/rollback edge — keyed by the
/// connection's <c>ClientConnectionId</c> — and drains the enlisted work then.
/// </summary>
/// <remarks>
/// This is intentionally <b>synchronous</b>. The ambient scope is stored in an <c>AsyncLocal</c>; a mutation made
/// inside an <c>async</c> method does not flow back to its caller, so an "open-and-enlist" async helper would strand
/// the ambient scope in its own frame and leave <c>ICurrentCommitCoordinator.Current</c> null for the caller's
/// subsequent work. Callers therefore open the transaction (sync or async) and then call this method <b>in their own
/// frame</b>, before doing the work that should enlist:
/// <code>
/// await using var connection = new SqlConnection(connectionString);
/// await connection.OpenAsync(ct);
/// await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
/// using var _ = connection.EnlistCommitCoordination(tx, services);
/// // publish / save here — ICurrentCommitCoordinator.Current is now this scope
/// await tx.CommitAsync(ct); // the diagnostic observer signals the scope on the durable commit edge
/// </code>
/// The returned scope's dispose is the un-signalled safety-net: if the connection's commit edge is never observed
/// (e.g. the connection is abandoned before commit), disposing without a signal discards the enlisted work.
/// </remarks>
[PublicAPI]
public static class EnlistCommitCoordinationExtensions
{
    extension(SqlConnection connection)
    {
        /// <summary>
        /// Pushes the ambient coordinated scope for an open SqlClient transaction, keyed by the connection's
        /// <c>ClientConnectionId</c> so the diagnostic observer can correlate the native commit/rollback edge.
        /// </summary>
        /// <param name="transaction">The open SqlClient transaction to coordinate.</param>
        /// <param name="services">The scoped service provider captured for the post-commit drain.</param>
        /// <param name="cancellationToken">
        /// Observed only while attaching (before any work is enlisted); a pre-cancelled token throws here rather
        /// than pushing an ambient scope. It does not govern the post-commit drain (design decision D9).
        /// </param>
        /// <returns>The coordinated scope; dispose it (after the transaction completes) to tear down.</returns>
        public ICommitScope EnlistCommitCoordination(
            SqlTransaction transaction,
            IServiceProvider services,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(transaction);
            Argument.IsNotNull(services);

            var signalSource = services.GetRequiredService<SqlServerCommitSignalSource>();

            return signalSource.Attach(
                new CommitCoordinatorBindings
                {
                    Services = services,
                    Capabilities = [new RelationalCommitContext(() => connection, () => transaction)],
                    ProviderTransactionKey = connection.ClientConnectionId,
                },
                cancellationToken
            );
        }
    }
}
