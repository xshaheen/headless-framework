// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination;
using Headless.CommitCoordination.EntityFramework;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Enlists an already-open EF transaction in commit coordination: pushes the ambient coordinated scope and
/// surfaces the live transaction through <see cref="IRelationalCommitContext" /> so participants (e.g. the
/// outbox writer) can enlist post-commit work that the registered
/// <see cref="CommitCoordinationTransactionInterceptor" /> drains when the transaction commits.
/// </summary>
/// <remarks>
/// This is intentionally <b>synchronous</b>. The ambient scope is stored in an <c>AsyncLocal</c>; a mutation made
/// inside an <c>async</c> method does not flow back to its caller, so an "open-and-enlist" async helper would
/// strand the ambient scope in its own frame and leave <c>ICurrentCommitCoordinator.Current</c> null for the
/// caller's subsequent work. Callers therefore open the transaction (sync or async) and then call this method
/// <b>in their own frame</b>, before doing the work that should enlist:
/// <code>
/// await using var tx = await db.Database.BeginTransactionAsync(ct);
/// await using var _ = db.Database.EnlistCommitCoordination(tx, services);
/// // publish / save here — ICurrentCommitCoordinator.Current is now this scope
/// await tx.CommitAsync(ct);
/// </code>
/// </remarks>
[PublicAPI]
public static class HeadlessEntityFrameworkEnlistCommitCoordinationExtensions
{
    extension(DatabaseFacade database)
    {
        /// <summary>
        /// Pushes the ambient coordinated scope for an open EF transaction. Dispose the returned scope to pop the
        /// ambient scope and discard enlisted work if the transaction was never signalled (un-signalled dispose
        /// rolls back).
        /// </summary>
        /// <param name="transaction">The open EF transaction to coordinate.</param>
        /// <param name="services">The scoped service provider captured for the post-commit drain.</param>
        /// <param name="cancellationToken">
        /// Observed only while attaching (before any work is enlisted); a pre-cancelled token throws here rather
        /// than pushing an ambient scope. It does not govern the post-commit drain (design decision D9).
        /// </param>
        /// <returns>The coordinated scope; dispose it (after the transaction completes) to tear down.</returns>
        public ICommitScope EnlistCommitCoordination(
            IDbContextTransaction transaction,
            IServiceProvider services,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(transaction);
            Argument.IsNotNull(services);

            var signalSource = services.GetRequiredService<EntityFrameworkCommitSignalSource>();
            var dbConnection = database.GetDbConnection();
            var dbTransaction = transaction.GetDbTransaction();

            return signalSource.Attach(
                new CommitCoordinatorBindings
                {
                    Services = services,
                    Capabilities = [new RelationalCommitContext(() => dbConnection, () => dbTransaction)],
                    ProviderTransactionKey = dbTransaction,
                },
                cancellationToken
            );
        }
    }
}
