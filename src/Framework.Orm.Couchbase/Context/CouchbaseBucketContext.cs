// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Reflection;
using Couchbase;
using Couchbase.Linq;
using Couchbase.Transactions;
using Couchbase.Transactions.Config;
using Microsoft.Extensions.Logging;

namespace Framework.Orm.Couchbase.Context;

[PublicAPI]
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

            logger.LogInformation(
                "[{TransactionId}] Transaction completed with UnstagingCompleted: {UnstagingCompleted} and logs {@Logs}",
                transactionResult.TransactionId,
                transactionResult.UnstagingComplete,
                transactionResult.Logs
            );
        }
        catch (Exception e)
        {
            logger.LogError(e, "Transaction failed");

            throw;
        }
    }

    #endregion
}
