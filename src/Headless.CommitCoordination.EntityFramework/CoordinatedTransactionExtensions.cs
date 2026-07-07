// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Single-call coordinated-transaction helpers for a plain <see cref="DbContext"/>: open a resilient
/// transaction, enlist it in commit coordination, run the operation, and commit — so deferred work buffered
/// inside the operation drains atomically on commit and is discarded on rollback. The enlist cannot be
/// forgotten because it is welded into the helper.
/// </summary>
/// <remarks>
/// A plain <see cref="DbContext"/> cannot expose the scope that resolved it, so these overloads require an
/// explicit <c>IServiceProvider</c> (the request scope) for the post-commit drain. A
/// <c>HeadlessDbContext</c> self-sources its scope, so its overloads omit the parameter. The block runs
/// inside the context's execution strategy (retry-safe); the enlist lives inside the retried delegate so a
/// retried attempt discards its buffer and the next attempt opens a fresh transaction and coordinator.
/// </remarks>
[PublicAPI]
public static class HeadlessEntityFrameworkCoordinatedTransactionExtensions
{
    /// <summary>
    /// Executes <paramref name="operation"/> inside a resilient, commit-coordinated transaction.
    /// </summary>
    /// <param name="context">The <see cref="DbContext"/> to operate on.</param>
    /// <param name="operation">An asynchronous delegate receiving the context and a cancellation token.</param>
    /// <param name="services">The scoped (request) service provider captured for the post-commit drain.</param>
    /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task ExecuteCoordinatedTransactionAsync(
        this DbContext context,
        Func<DbContext, CancellationToken, Task> operation,
        IServiceProvider services,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
    {
        var state = (Operation: operation, Isolation: isolation, Context: context, Services: services);

        return context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    var transaction = await state
                        .Context.Database.BeginTransactionAsync(state.Isolation, ct)
                        .ConfigureAwait(false);

                    await using (transaction.ConfigureAwait(false))
                    {
                        await using var _ = state
                            .Context.Database.EnlistCommitCoordination(transaction, state.Services, ct)
                            .ConfigureAwait(false);

                        await state.Operation(state.Context, ct).ConfigureAwait(false);
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }
                },
                cancellationToken
            );
    }

    /// <inheritdoc cref="ExecuteCoordinatedTransactionAsync(DbContext, Func{DbContext, CancellationToken, Task}, IServiceProvider, IsolationLevel, CancellationToken)"/>
    /// <typeparam name="TArg">Type of the argument passed to <paramref name="operation"/>.</typeparam>
    /// <param name="arg">Argument forwarded to <paramref name="operation"/>.</param>
    public static Task ExecuteCoordinatedTransactionAsync<TArg>(
        this DbContext context,
        Func<TArg, DbContext, CancellationToken, Task> operation,
        TArg arg,
        IServiceProvider services,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
    {
        var state = (Operation: operation, Arg: arg, Isolation: isolation, Context: context, Services: services);

        return context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    var transaction = await state
                        .Context.Database.BeginTransactionAsync(state.Isolation, ct)
                        .ConfigureAwait(false);

                    await using (transaction.ConfigureAwait(false))
                    {
                        await using var _ = state
                            .Context.Database.EnlistCommitCoordination(transaction, state.Services, ct)
                            .ConfigureAwait(false);

                        await state.Operation(state.Arg, state.Context, ct).ConfigureAwait(false);
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }
                },
                cancellationToken
            );
    }

    /// <summary>
    /// Executes <paramref name="operation"/> inside a resilient, commit-coordinated transaction and returns its result.
    /// </summary>
    /// <typeparam name="TResult">Type of the value returned by the operation.</typeparam>
    /// <param name="context">The <see cref="DbContext"/> to operate on.</param>
    /// <param name="operation">An asynchronous delegate receiving the context and a cancellation token, returning a result.</param>
    /// <param name="services">The scoped (request) service provider captured for the post-commit drain.</param>
    /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result produced by <paramref name="operation"/>.</returns>
    public static Task<TResult> ExecuteCoordinatedTransactionAsync<TResult>(
        this DbContext context,
        Func<DbContext, CancellationToken, Task<TResult>> operation,
        IServiceProvider services,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
    {
        var state = (Operation: operation, Isolation: isolation, Context: context, Services: services);

        return context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    var transaction = await state
                        .Context.Database.BeginTransactionAsync(state.Isolation, ct)
                        .ConfigureAwait(false);

                    await using (transaction.ConfigureAwait(false))
                    {
                        await using var _ = state
                            .Context.Database.EnlistCommitCoordination(transaction, state.Services, ct)
                            .ConfigureAwait(false);

                        var result = await state.Operation(state.Context, ct).ConfigureAwait(false);
                        await transaction.CommitAsync(ct).ConfigureAwait(false);

                        return result;
                    }
                },
                cancellationToken
            );
    }

    /// <inheritdoc cref="ExecuteCoordinatedTransactionAsync{TResult}(DbContext, Func{DbContext, CancellationToken, Task{TResult}}, IServiceProvider, IsolationLevel, CancellationToken)"/>
    /// <typeparam name="TArg">Type of the argument passed to <paramref name="operation"/>.</typeparam>
    /// <param name="arg">Argument forwarded to <paramref name="operation"/>.</param>
    public static Task<TResult> ExecuteCoordinatedTransactionAsync<TResult, TArg>(
        this DbContext context,
        Func<TArg, DbContext, CancellationToken, Task<TResult>> operation,
        TArg arg,
        IServiceProvider services,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
    {
        var state = (Operation: operation, Arg: arg, Isolation: isolation, Context: context, Services: services);

        return context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    var transaction = await state
                        .Context.Database.BeginTransactionAsync(state.Isolation, ct)
                        .ConfigureAwait(false);

                    await using (transaction.ConfigureAwait(false))
                    {
                        await using var _ = state
                            .Context.Database.EnlistCommitCoordination(transaction, state.Services, ct)
                            .ConfigureAwait(false);

                        var result = await state.Operation(state.Arg, state.Context, ct).ConfigureAwait(false);
                        await transaction.CommitAsync(ct).ConfigureAwait(false);

                        return result;
                    }
                },
                cancellationToken
            );
    }
}
