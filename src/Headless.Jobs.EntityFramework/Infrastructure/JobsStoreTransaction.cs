// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Runs a Jobs store write that needs its own transaction inside the context's execution strategy. A retrying strategy
/// (<c>EnableRetryOnFailure</c>) refuses a transaction begun outside it, and replays the whole attempt, transaction
/// included, on a transient fault; the default strategy runs the attempt once. Each attempt gets a fresh context, so a
/// replay never sees the failed attempt's tracked entities.
/// </summary>
internal static class JobsStoreTransaction
{
    public static async Task ExecuteAsync<TDbContext>(
        IDbContextFactory<TDbContext> dbContextFactory,
        Func<JobsStoreTransactionAttempt<TDbContext>, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
        where TDbContext : DbContext
    {
        await ExecuteAsync(
                dbContextFactory,
                async (attempt, ct) =>
                {
                    await operation(attempt, ct).ConfigureAwait(false);
                    return true;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="operation" /> in a fresh context and transaction per attempt. An attempt that returns
    /// without calling <see cref="JobsStoreTransactionAttempt{TDbContext}.CommitAsync" /> rolls back. A fault raised
    /// once the commit started is rethrown and never replayed, because the commit may already be durable.
    /// </summary>
    public static async Task<TResult> ExecuteAsync<TDbContext, TResult>(
        IDbContextFactory<TDbContext> dbContextFactory,
        Func<JobsStoreTransactionAttempt<TDbContext>, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    )
        where TDbContext : DbContext
    {
        await using var strategyContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var (result, error) = await strategyContext
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async ct =>
                {
                    JobsStoreTransactionAttempt<TDbContext>? attempt = null;
                    try
                    {
                        await using var context = await dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                        await using var transaction = await context
                            .Database.BeginTransactionAsync(ct)
                            .ConfigureAwait(false);
                        attempt = new JobsStoreTransactionAttempt<TDbContext>(context, transaction);
                        var value = await operation(attempt, ct).ConfigureAwait(false);
                        // The explicit nullable cast fixes the tuple's Error type; without it the lambda's two returns
                        // infer a non-nullable ExceptionDispatchInfo and the deconstruction below fails CS8619.
                        return (Result: value, Error: (ExceptionDispatchInfo?)null);
                    }
                    catch (Exception exception) when (attempt?.CommitStarted == true)
                    {
                        // A commit, post-commit, or disposal fault may follow a successful commit; returning it as the
                        // attempt's outcome keeps the strategy from replaying it speculatively.
                        return (Result: default!, Error: ExceptionDispatchInfo.Capture(exception));
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        error?.Throw();
        return result;
    }
}

/// <summary>One attempt of <see cref="JobsStoreTransaction" />: its context, its transaction, and its commit.</summary>
internal sealed class JobsStoreTransactionAttempt<TDbContext>(TDbContext dbContext, IDbContextTransaction transaction)
    where TDbContext : DbContext
{
    public TDbContext DbContext { get; } = dbContext;

    public IDbContextTransaction Transaction { get; } = transaction;

    internal bool CommitStarted { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        CommitStarted = true;
        return Transaction.CommitAsync(cancellationToken);
    }
}
