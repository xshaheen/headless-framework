// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.CommitCoordination.EntityFramework;
using Headless.EntityFramework;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Single-call coordinated-transaction helpers for any Headless-managed context (<see cref="HeadlessDbContext"/>
/// and the Headless Identity context — any <see cref="IHeadlessDbContext"/>): open a resilient transaction,
/// enlist it in commit coordination, run the operation, and commit — so deferred work (outbox dispatch, durable
/// jobs) buffered inside the operation drains atomically on commit and is discarded on rollback. The enlist
/// cannot be forgotten because it is welded into the helper.
/// </summary>
/// <remarks>
/// <para>
/// These overloads self-source the scoped (request) service provider from the context, so the caller
/// does not pass one. A plain <see cref="DbContext"/> (or raw ADO connection) cannot expose its resolving
/// scope, so those overloads require an explicit <c>IServiceProvider</c>.
/// </para>
/// <para>
/// The whole block runs inside the context's execution strategy, so it is safe with retrying providers
/// (e.g. SQL Server with <c>EnableRetryOnFailure</c>). The enlist lives <b>inside</b> the retried delegate:
/// a transient-fault retry disposes the enlist scope un-signalled (discarding its buffer) and the next
/// attempt opens a fresh transaction and coordinator — no double-dispatch, no stale buffer.
/// </para>
/// </remarks>
[PublicAPI]
public static class HeadlessCoordinatedTransactionExtensions
{
    /// <summary>
    /// Executes <paramref name="operation"/> inside a resilient, commit-coordinated transaction. The caller
    /// is responsible for calling <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> within the
    /// operation; publishes made inside it enlist on the ambient coordinator and dispatch on commit.
    /// </summary>
    /// <typeparam name="TContext">The Headless-managed context type (a <see cref="DbContext"/> implementing <see cref="IHeadlessDbContext"/>).</typeparam>
    /// <param name="context">The Headless-managed context to operate on.</param>
    /// <param name="operation">An asynchronous delegate receiving the context and a cancellation token.</param>
    /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task ExecuteCoordinatedTransactionAsync<TContext>(
        this TContext context,
        Func<TContext, CancellationToken, Task> operation,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
        where TContext : DbContext, IHeadlessDbContext
    {
        var services = (context).ServiceProvider;
        var state = (Operation: operation, Isolation: isolation, Context: context, Services: services);

        return context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    await using var transaction = await state
                        .Context.Database.BeginTransactionAsync(state.Isolation, ct)
                        .ConfigureAwait(false);

                    // Enlist SYNCHRONOUSLY, in this frame, so the ambient coordinator flows to the operation's
                    // publishes. An AsyncLocal push inside an async helper would not propagate to this delegate.
                    await using var _ = state
                        .Context.Database.EnlistCommitCoordination(transaction, state.Services, ct)
                        .ConfigureAwait(false);

                    await state.Operation(state.Context, ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                },
                cancellationToken
            );
    }

    /// <inheritdoc cref="ExecuteCoordinatedTransactionAsync{TContext}(TContext, Func{TContext, CancellationToken, Task}, IsolationLevel, CancellationToken)"/>
    /// <typeparam name="TArg">Type of the argument passed to <paramref name="operation"/>.</typeparam>
    /// <param name="arg">Argument forwarded to <paramref name="operation"/>.</param>
    public static Task ExecuteCoordinatedTransactionAsync<TContext, TArg>(
        this TContext context,
        Func<TArg, TContext, CancellationToken, Task> operation,
        TArg arg,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
        where TContext : DbContext, IHeadlessDbContext
    {
        var services = (context).ServiceProvider;
        var state = (Operation: operation, Arg: arg, Isolation: isolation, Context: context, Services: services);

        return context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    await using var transaction = await state
                        .Context.Database.BeginTransactionAsync(state.Isolation, ct)
                        .ConfigureAwait(false);

                    await using var _ = state
                        .Context.Database.EnlistCommitCoordination(transaction, state.Services, ct)
                        .ConfigureAwait(false);

                    await state.Operation(state.Arg, state.Context, ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                },
                cancellationToken
            );
    }

    /// <summary>
    /// Executes <paramref name="operation"/> inside a resilient, commit-coordinated transaction and returns
    /// its result. Publishes made inside it enlist on the ambient coordinator and dispatch on commit.
    /// </summary>
    /// <typeparam name="TContext">The Headless-managed context type (a <see cref="DbContext"/> implementing <see cref="IHeadlessDbContext"/>).</typeparam>
    /// <typeparam name="TResult">Type of the value returned by the operation.</typeparam>
    /// <param name="context">The Headless-managed context to operate on.</param>
    /// <param name="operation">An asynchronous delegate receiving the context and a cancellation token, returning a result.</param>
    /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result produced by <paramref name="operation"/>.</returns>
    public static Task<TResult> ExecuteCoordinatedTransactionAsync<TContext, TResult>(
        this TContext context,
        Func<TContext, CancellationToken, Task<TResult>> operation,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
        where TContext : DbContext, IHeadlessDbContext
    {
        var services = (context).ServiceProvider;
        var state = (Operation: operation, Isolation: isolation, Context: context, Services: services);

        return context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    await using var transaction = await state
                        .Context.Database.BeginTransactionAsync(state.Isolation, ct)
                        .ConfigureAwait(false);

                    await using var _ = state
                        .Context.Database.EnlistCommitCoordination(transaction, state.Services, ct)
                        .ConfigureAwait(false);

                    var result = await state.Operation(state.Context, ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);

                    return result;
                },
                cancellationToken
            );
    }

    /// <inheritdoc cref="ExecuteCoordinatedTransactionAsync{TContext, TResult}(TContext, Func{TContext, CancellationToken, Task{TResult}}, IsolationLevel, CancellationToken)"/>
    /// <typeparam name="TArg">Type of the argument passed to <paramref name="operation"/>.</typeparam>
    /// <param name="arg">Argument forwarded to <paramref name="operation"/>.</param>
    public static Task<TResult> ExecuteCoordinatedTransactionAsync<TContext, TResult, TArg>(
        this TContext context,
        Func<TArg, TContext, CancellationToken, Task<TResult>> operation,
        TArg arg,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
        where TContext : DbContext, IHeadlessDbContext
    {
        var services = (context).ServiceProvider;
        var state = (Operation: operation, Arg: arg, Isolation: isolation, Context: context, Services: services);

        return context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    await using var transaction = await state
                        .Context.Database.BeginTransactionAsync(state.Isolation, ct)
                        .ConfigureAwait(false);

                    await using var _ = state
                        .Context.Database.EnlistCommitCoordination(transaction, state.Services, ct)
                        .ConfigureAwait(false);

                    var result = await state.Operation(state.Arg, state.Context, ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);

                    return result;
                },
                cancellationToken
            );
    }
}
