// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Couchbase;
using Couchbase.Linq;
using Couchbase.Transactions;
using Couchbase.Transactions.Config;
using Microsoft.Extensions.Logging;

namespace Headless.Couchbase.Context;

/// <summary>
/// Base Couchbase bucket context that exposes scope-and-collection-aware N1QL querying and a
/// high-level transaction API. Subclass this type and declare <c>IDocumentSet&lt;T&gt;</c> properties
/// annotated with <c>CouchbaseCollectionAttribute</c> to map entity types to collections.
/// </summary>
/// <remarks>
/// Instances are created and initialized by <c>CouchbaseBucketContextInitializer</c>, which
/// reflectively wires each <c>IDocumentSet&lt;T&gt;</c> property to its scope and collection. Do not
/// construct subclasses manually; use <c>IBucketContextProvider</c> instead.
/// </remarks>
public class CouchbaseBucketContext(IBucket bucket, Transactions transactions, ILogger<CouchbaseBucketContext> logger)
    : BucketContext(bucket)
{
    #region Query

    private static readonly MethodInfo _QueryMethod =
        typeof(BucketContext).GetMethod(
            name: "Query",
            bindingAttr: BindingFlags.Instance | BindingFlags.NonPublic,
            types: [typeof(string), typeof(string), typeof(BucketQueryOptions)]
        )
        ?? throw new InvalidOperationException(
            "NonPublic|Instance Query(string, string, BucketQueryOptions) method not found in BucketContext class."
        );

    /// <summary>
    /// Returns an <see cref="IQueryable{T}"/> scoped to the specified Couchbase scope and collection.
    /// Delegates to the non-public <c>BucketContext.Query</c> overload via reflection.
    /// </summary>
    /// <typeparam name="T">The document type to query.</typeparam>
    /// <param name="scope">The Couchbase scope name.</param>
    /// <param name="collection">The Couchbase collection name.</param>
    /// <param name="options">Optional query options controlling scan consistency and other behavior.</param>
    /// <returns>A LINQ queryable targeting the specified scope and collection.</returns>
    public IQueryable<T> Query<T>(string scope, string collection, BucketQueryOptions options = BucketQueryOptions.None)
    {
        var queryable =
            _QueryMethod.MakeGenericMethod(typeof(T)).Invoke(this, [scope, collection, options])
            ?? throw new InvalidOperationException("Cannot be null");

        return (IQueryable<T>)queryable;
    }

    #endregion

    #region Transactions

    /// <summary>
    /// Executes <paramref name="operation"/> within a Couchbase ACID transaction. The operation
    /// receives an <c>AttemptContext</c> and returns <see langword="true"/> to commit or
    /// <see langword="false"/> to roll back.
    /// </summary>
    /// <param name="operation">
    /// The transactional work to perform. Return <see langword="true"/> to commit, or
    /// <see langword="false"/> (or throw) to roll back.
    /// </param>
    /// <param name="config">Optional per-transaction overrides (timeout, durability, etc.).</param>
    public async Task ExecuteTransactionAsync(
        Func<AttemptContext, Task<bool>> operation,
        PerTransactionConfig? config = null
    )
    {
        config ??= PerTransactionConfigBuilder.Create().Build();

        try
        {
            var transactionResult = await transactions.RunAsync(
                async ctx =>
                {
                    bool commit;

                    try
                    {
                        commit = await operation(ctx);
                    }
                    catch
                    {
                        await ctx.RollbackAsync();

                        throw;
                    }

                    if (commit)
                    {
                        await ctx.CommitAsync();
                    }
                    else
                    {
                        await ctx.RollbackAsync();
                    }
                },
                config
            );

            logger.LogTransactionCompleted(
                transactionResult.TransactionId,
                transactionResult.UnstagingComplete,
                transactionResult.Logs
            );
        }
        catch (Exception e)
        {
            logger.LogTransactionFailed(e);

            throw;
        }
    }

    #endregion
}

internal static partial class CouchbaseBucketContextLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "TransactionCompleted",
        Level = LogLevel.Information,
        Message = "[{TransactionId}] Transaction completed with UnstagingCompleted: {UnstagingCompleted} and logs {Logs}"
    )]
    public static partial void LogTransactionCompleted(
        this ILogger logger,
        string? transactionId,
        bool unstagingCompleted,
        IEnumerable<string> logs
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "TransactionFailed",
        Level = LogLevel.Error,
        Message = "Transaction failed"
    )]
    public static partial void LogTransactionFailed(this ILogger logger, Exception exception);
}
