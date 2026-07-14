// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Runtime.ExceptionServices;

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
/// inside the context's execution strategy. Failures before commit starts may retry with a fresh transaction and
/// coordinator. Once commit starts, any exception is surfaced without replay because the database outcome may be
/// unknown; use client-generated keys or another idempotency key to reconcile that outcome safely.
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
    ) =>
        _ExecuteCoreAsync(
            context,
            async (dbContext, ct) =>
            {
                await operation(dbContext, ct).ConfigureAwait(false);
                return true;
            },
            services,
            isolation,
            cancellationToken
        );

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
    ) =>
        _ExecuteCoreAsync(
            context,
            async (dbContext, ct) =>
            {
                await operation(arg, dbContext, ct).ConfigureAwait(false);
                return true;
            },
            services,
            isolation,
            cancellationToken
        );

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
    ) => _ExecuteCoreAsync(context, operation, services, isolation, cancellationToken);

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
    ) =>
        _ExecuteCoreAsync(
            context,
            (dbContext, ct) => operation(arg, dbContext, ct),
            services,
            isolation,
            cancellationToken
        );

    private static async Task<TResult> _ExecuteCoreAsync<TResult>(
        DbContext context,
        Func<DbContext, CancellationToken, Task<TResult>> operation,
        IServiceProvider services,
        IsolationLevel isolation,
        CancellationToken cancellationToken
    )
    {
        var state = (Operation: operation, Isolation: isolation, Context: context, Services: services);

        var (result, error) = await context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    var commitStarted = false;
                    var result = default(TResult)!;
                    try
                    {
                        await using var transaction = await state
                            .Context.Database.BeginTransactionAsync(state.Isolation, ct)
                            .ConfigureAwait(false);
                        await using var _ = state
                            .Context.Database.EnlistCommitCoordination(transaction, state.Services, ct)
                            .ConfigureAwait(false);

                        result = await state.Operation(state.Context, ct).ConfigureAwait(false);
                        commitStarted = true;
                        await transaction.CommitAsync(ct).ConfigureAwait(false);

                        return (Result: result, Error: (ExceptionDispatchInfo?)null);
                    }
                    catch (Exception ex) when (commitStarted)
                    {
                        return (Result: result, Error: ExceptionDispatchInfo.Capture(ex));
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        error?.Throw();

        return result;
    }
}
