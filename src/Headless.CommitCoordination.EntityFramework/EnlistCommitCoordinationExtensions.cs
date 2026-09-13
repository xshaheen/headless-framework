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
/// await using var scope = db.Database.EnlistCommitCoordination(tx, services);
/// // publish / save here — ICurrentCommitCoordinator.Current is now this scope
/// await tx.CommitAsync(ct); // the interceptor signals the scope on the commit edge
/// </code>
/// The interceptor signals the outcome, so no explicit signal is needed. A caller that must settle the outcome
/// itself — for example after a commit that threw client-side but was confirmed committed by a probe — calls
/// <see cref="ICommitScope.SignalAsync" /> on the returned scope; a repeated signal with the same outcome is a
/// silent no-op. Disposing the scope without any signal discards the enlisted work.
/// </remarks>
[PublicAPI]
public static class HeadlessEntityFrameworkEnlistCommitCoordinationExtensions
{
    extension(DatabaseFacade database)
    {
        /// <summary>
        /// Pushes the ambient coordinated scope for an open EF transaction and registers it with the commit
        /// interceptor. Dispose the returned scope after the transaction completes; an un-signalled dispose
        /// discards the enlisted work.
        /// </summary>
        /// <param name="transaction">The open EF transaction to coordinate.</param>
        /// <param name="services">A service provider that resolves the commit coordination services.</param>
        /// <param name="cancellationToken">
        /// Observed only before the scope is pushed; a pre-cancelled token throws here rather than pushing an
        /// ambient scope. It does not govern the post-commit drain, which always runs to completion.
        /// </param>
        /// <returns>The coordinated scope; the caller owns it and disposes it after the transaction completes.</returns>
        /// <exception cref="InvalidOperationException">
        /// <c>AddEntityFrameworkCommitCoordination</c> was not called, or a scope is already enlisted for
        /// <paramref name="transaction" />.
        /// </exception>
        public ICommitScope EnlistCommitCoordination(
            IDbContextTransaction transaction,
            IServiceProvider services,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(transaction);
            Argument.IsNotNull(services);
            cancellationToken.ThrowIfCancellationRequested();

            var interceptor = services.GetRequiredService<CommitCoordinationTransactionInterceptor>();
            var scopeFactory = services.GetRequiredService<ICommitScopeFactory>();
            var dbConnection = database.GetDbConnection();
            var dbTransaction = transaction.GetDbTransaction();

            return interceptor.Enlist(
                scopeFactory,
                new RelationalCommitContext(() => dbConnection, () => dbTransaction),
                dbTransaction
            );
        }
    }
}
