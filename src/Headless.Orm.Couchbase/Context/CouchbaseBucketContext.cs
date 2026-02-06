// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Couchbase;
using Couchbase.Linq;
using Couchbase.Transactions;
using Couchbase.Transactions.Config;
using Microsoft.Extensions.Logging;

namespace Headless.Orm.Couchbase.Context;

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

    public IQueryable<T> Query<T>(string scope, string collection, BucketQueryOptions options = BucketQueryOptions.None)
    {
        var queryable =
            _QueryMethod.MakeGenericMethod(typeof(T)).Invoke(this, [scope, collection, options])
            ?? throw new InvalidOperationException("Cannot be null");

        return (IQueryable<T>)queryable;
    }

    #endregion

    #region Transactions

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
