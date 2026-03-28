// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods for executing operations within a resilient transaction that is
/// coordinated with the <see cref="DbContext"/>'s execution strategy.
/// </summary>
public static class DbContextTransactionExtensions
{
    /// <summary>
    /// Executes <paramref name="operation"/> inside a resilient transaction. The entire block is
    /// wrapped in the context's execution strategy so it is safe with retrying providers
    /// (e.g. SQL Server with <c>EnableRetryOnFailure</c>).
    /// </summary>
    /// <param name="context">The <see cref="DbContext"/> to operate on.</param>
    /// <param name="operation">
    /// An asynchronous delegate that receives the <see cref="DbContext"/> and a
    /// <see cref="CancellationToken"/>. The caller is responsible for calling
    /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> within the operation.
    /// </param>
    /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task ExecuteTransactionAsync(
        this DbContext context,
        Func<DbContext, CancellationToken, Task> operation,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
    {
        var state = (Operation: operation, Isolation: isolation, Context: context);

        return context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    await using var transaction = await state
                        .Context.Database.BeginTransactionAsync(state.Isolation, ct)
                        .ConfigureAwait(false);

                    await state.Operation(state.Context, ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                },
                cancellationToken
            );
    }

    /// <inheritdoc cref="ExecuteTransactionAsync(DbContext, Func{DbContext, CancellationToken, Task}, IsolationLevel, CancellationToken)"/>
    /// <typeparam name="TArg">Type of the argument passed to <paramref name="operation"/>.</typeparam>
    /// <param name="arg">Argument forwarded to <paramref name="operation"/>.</param>
    public static Task ExecuteTransactionAsync<TArg>(
        this DbContext context,
        Func<TArg, DbContext, CancellationToken, Task> operation,
        TArg arg,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
    {
        var state = (Operation: operation, Arg: arg, Isolation: isolation, Context: context);

        return context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    await using var transaction = await state
                        .Context.Database.BeginTransactionAsync(state.Isolation, ct)
                        .ConfigureAwait(false);

                    await state.Operation(state.Arg, state.Context, ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                },
                cancellationToken
            );
    }

    /// <summary>
    /// Executes <paramref name="operation"/> inside a resilient transaction and returns a result.
    /// The entire block is wrapped in the context's execution strategy so it is safe with retrying
    /// providers (e.g. SQL Server with <c>EnableRetryOnFailure</c>).
    /// </summary>
    /// <typeparam name="TResult">Type of the value returned by the operation.</typeparam>
    /// <param name="context">The <see cref="DbContext"/> to operate on.</param>
    /// <param name="operation">
    /// An asynchronous delegate that receives the <see cref="DbContext"/> and a
    /// <see cref="CancellationToken"/>, and returns a result. The caller is responsible for
    /// calling <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> within the operation.
    /// </param>
    /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result produced by <paramref name="operation"/>.</returns>
    public static Task<TResult> ExecuteTransactionAsync<TResult>(
        this DbContext context,
        Func<DbContext, CancellationToken, Task<TResult>> operation,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
    {
        var state = (Operation: operation, Isolation: isolation, Context: context);

        return context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    await using var transaction = await state
                        .Context.Database.BeginTransactionAsync(state.Isolation, ct)
                        .ConfigureAwait(false);

                    var result = await state.Operation(state.Context, ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);

                    return result;
                },
                cancellationToken
            );
    }

    /// <inheritdoc cref="ExecuteTransactionAsync{TResult}(DbContext, Func{DbContext, CancellationToken, Task{TResult}}, IsolationLevel, CancellationToken)"/>
    /// <typeparam name="TArg">Type of the argument passed to <paramref name="operation"/>.</typeparam>
    /// <param name="arg">Argument forwarded to <paramref name="operation"/>.</param>
    public static Task<TResult> ExecuteTransactionAsync<TResult, TArg>(
        this DbContext context,
        Func<TArg, DbContext, CancellationToken, Task<TResult>> operation,
        TArg arg,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
    {
        var state = (Operation: operation, Arg: arg, Isolation: isolation, Context: context);

        return context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    await using var transaction = await state
                        .Context.Database.BeginTransactionAsync(state.Isolation, ct)
                        .ConfigureAwait(false);

                    var result = await state.Operation(state.Arg, state.Context, ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);

                    return result;
                },
                cancellationToken
            );
    }
}
