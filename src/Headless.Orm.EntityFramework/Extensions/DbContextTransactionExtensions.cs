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
    /// Executes <paramref name="operation"/> inside a resilient transaction. The operation returns
    /// <see langword="true"/> to commit or <see langword="false"/> to roll back. When the operation
    /// returns <see langword="true"/>, <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>
    /// is called before committing. The entire block is wrapped in the context's execution strategy
    /// so it is safe with retrying providers (e.g. SQL Server with <c>EnableRetryOnFailure</c>).
    /// </summary>
    /// <param name="context">The <see cref="DbContext"/> to operate on.</param>
    /// <param name="operation">
    /// An asynchronous delegate that returns <see langword="true"/> to commit or
    /// <see langword="false"/> to roll back.
    /// </param>
    /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task ExecuteTransactionAsync(
        this DbContext context,
        Func<Task<bool>> operation,
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
                    await using var transaction = await state.Context.Database.BeginTransactionAsync(
                        state.Isolation,
                        ct
                    );

                    bool commit;

                    try
                    {
                        commit = await state.Operation();

                        if (commit)
                        {
                            await state.Context.SaveChangesAsync(ct).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);

                        throw;
                    }

                    if (commit)
                    {
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    }
                },
                cancellationToken
            );
    }

    /// <inheritdoc cref="ExecuteTransactionAsync(DbContext, Func{Task{bool}}, IsolationLevel, CancellationToken)"/>
    /// <typeparam name="TArg">Type of the argument passed to <paramref name="operation"/>.</typeparam>
    /// <param name="arg">Argument forwarded to <paramref name="operation"/>.</param>
    public static Task ExecuteTransactionAsync<TArg>(
        this DbContext context,
        Func<TArg, Task<bool>> operation,
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
                    await using var transaction = await state.Context.Database.BeginTransactionAsync(
                        state.Isolation,
                        ct
                    );

                    bool commit;

                    try
                    {
                        commit = await state.Operation(state.Arg);

                        if (commit)
                        {
                            await state.Context.SaveChangesAsync(ct).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);

                        throw;
                    }

                    if (commit)
                    {
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    }
                },
                cancellationToken
            );
    }

    /// <summary>
    /// Executes <paramref name="operation"/> inside a resilient transaction and returns a result.
    /// The operation returns a tuple of (<see langword="bool"/> commit, <typeparamref name="TResult"/>? result).
    /// When commit is <see langword="true"/>, <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>
    /// is called before committing.
    /// </summary>
    /// <typeparam name="TResult">Type of the value returned by the operation.</typeparam>
    /// <param name="context">The <see cref="DbContext"/> to operate on.</param>
    /// <param name="operation">
    /// An asynchronous delegate that returns a commit flag and an optional result.
    /// </param>
    /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result produced by <paramref name="operation"/>.</returns>
    public static Task<TResult?> ExecuteTransactionAsync<TResult>(
        this DbContext context,
        Func<Task<(bool, TResult?)>> operation,
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
                    await using var transaction = await state.Context.Database.BeginTransactionAsync(
                        state.Isolation,
                        ct
                    );

                    TResult? result;
                    bool commit;

                    try
                    {
                        (commit, result) = await state.Operation();

                        if (commit)
                        {
                            await state.Context.SaveChangesAsync(ct).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);

                        throw;
                    }

                    if (commit)
                    {
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    }

                    return result;
                },
                cancellationToken
            );
    }

    /// <inheritdoc cref="ExecuteTransactionAsync{TResult}(DbContext, Func{Task{ValueTuple{bool, TResult}}}, IsolationLevel, CancellationToken)"/>
    /// <typeparam name="TArg">Type of the argument passed to <paramref name="operation"/>.</typeparam>
    /// <param name="arg">Argument forwarded to <paramref name="operation"/>.</param>
    public static Task<TResult?> ExecuteTransactionAsync<TResult, TArg>(
        this DbContext context,
        Func<TArg, Task<(bool, TResult?)>> operation,
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
                    await using var transaction = await state.Context.Database.BeginTransactionAsync(
                        state.Isolation,
                        ct
                    );

                    TResult? result;
                    bool commit;

                    try
                    {
                        (commit, result) = await state.Operation(state.Arg);

                        if (commit)
                        {
                            await state.Context.SaveChangesAsync(ct).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);

                        throw;
                    }

                    if (commit)
                    {
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    }

                    return result;
                },
                cancellationToken
            );
    }
}
