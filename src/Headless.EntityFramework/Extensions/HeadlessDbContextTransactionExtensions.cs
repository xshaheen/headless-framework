// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Checks;
using Headless.EntityFramework;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Single-call unit-of-work helpers for any Headless-managed context (<see cref="HeadlessDbContext"/> and the
/// Identity context — any <see cref="IHeadlessDbContext"/>): begin a unit of work on the context inside its
/// execution strategy, run the operation, and complete it — so work enlisted inside the operation (outbox rows,
/// durable jobs, <c>OnCompleted</c> registrations) commits atomically with the entity batch and drains after
/// the commit. The unit of work cannot be forgotten because it is welded into the helper.
/// </summary>
/// <remarks>
/// <para>
/// A thin wrapper over <c>IUnitOfWorkManager.RunAsync(db, …)</c> that self-sources the scoped manager from the
/// context (<see cref="IHeadlessDbContext.ServiceProvider"/>), so the caller passes neither a manager nor a
/// provider. A plain <see cref="DbContext"/> cannot expose its resolving scope; call
/// <c>IUnitOfWorkManager.RunAsync(db, …)</c> directly for one.
/// </para>
/// <para>
/// Replay semantics are those of <c>RunAsync</c>: a failure before the commit starts may replay the whole block
/// with a fresh transaction and unit; once the commit has started, or after <c>IUnitOfWork.PreventRetry</c>,
/// the fault surfaces without replay because the database outcome may be unknown — use client-generated keys
/// or another idempotency key to reconcile it.
/// </para>
/// </remarks>
[PublicAPI]
public static class HeadlessDbContextTransactionExtensions
{
    extension<TContext>(TContext context)
        where TContext : DbContext, IHeadlessDbContext
    {
        /// <summary>
        /// Runs <paramref name="operation"/> inside a unit of work begun on this context. The caller is
        /// responsible for calling <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> within the
        /// operation; publishes and job writes made inside it enlist on the unit and dispatch after the commit.
        /// </summary>
        /// <param name="operation">An asynchronous delegate receiving the context and a cancellation token.</param>
        /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</param>
        /// <param name="cancellationToken">Cancellation token forwarded to begin, commit, and the operation.</param>
        public Task ExecuteTransactionAsync(
            Func<TContext, CancellationToken, Task> operation,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(context);
            Argument.IsNotNull(operation);

            return _Manager(context).RunAsync(context, (_, ct) => operation(context, ct), isolation, cancellationToken);
        }

        /// <summary>
        /// Runs <paramref name="operation"/> inside a unit of work begun on this context and returns its result,
        /// with the same replay semantics as the result-less overload.
        /// </summary>
        /// <typeparam name="TResult">Type of the value returned by the operation.</typeparam>
        /// <param name="operation">An asynchronous delegate receiving the context and a cancellation token, returning a result.</param>
        /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</param>
        /// <param name="cancellationToken">Cancellation token forwarded to begin, commit, and the operation.</param>
        /// <returns>The result produced by <paramref name="operation"/>.</returns>
        public Task<TResult> ExecuteTransactionAsync<TResult>(
            Func<TContext, CancellationToken, Task<TResult>> operation,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(context);
            Argument.IsNotNull(operation);

            return _Manager(context).RunAsync(context, (_, ct) => operation(context, ct), isolation, cancellationToken);
        }
    }

    // The context's own scope: for a factory-created context that is the scope the factory opened, so the unit
    // lands on the manager the save pipeline in that scope consults first.
    private static IUnitOfWorkManager _Manager(IHeadlessDbContext context)
    {
        return context.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
    }
}
