// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Couchbase;
using Couchbase.Transactions;
using Framework.Checks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace Framework.Orm.Couchbase.Clusters;

using GetClusterResult = (ICluster Cluster, Transactions ClusterTransactions);

public interface ICouchbaseClustersProvider : IAsyncDisposable
{
    ValueTask<GetClusterResult> GetClusterAsync(string clusterKey);
}

public sealed class CouchbaseClustersProvider(
    ICouchbaseClusterOptionsProvider clusterOptionsProvider,
    ICouchbaseTransactionConfigProvider transactionConfigProvider,
    ILogger<CouchbaseClustersProvider> logger
) : ICouchbaseClustersProvider
{
    private static readonly ConcurrentDictionary<string, AsyncLazy<GetClusterResult>> _Clusters = new(
        StringComparer.Ordinal
    );

    public async ValueTask<GetClusterResult> GetClusterAsync(string clusterKey)
    {
        Argument.IsNotEmpty(clusterKey);

        return await _Clusters.GetOrAdd(
            clusterKey,
            static (clusterKey, @this) => @this._CreateLazyClusterAsync(clusterKey),
            this
        );
    }

    private AsyncLazy<GetClusterResult> _CreateLazyClusterAsync(string clusterKey)
    {
        return new(async () =>
        {
            var clusterOptions = await clusterOptionsProvider.GetAsync(clusterKey);
            var cluster = await Cluster.ConnectAsync(clusterOptions);

            var transactionConfig = await transactionConfigProvider.GetAsync(clusterKey);
            var transactions = Transactions.Create(cluster, transactionConfig);

            try
            {
                await cluster.WaitUntilReadyAsync(TimeSpan.FromMinutes(1));
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to connect to the cluster {ClusterKey}", clusterKey);
            }

            return (cluster, transactions);
        });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var item in _Clusters)
        {
            if (item.Value is not { IsStarted: true, Task: { IsCompleted: true, IsFaulted: false, IsCanceled: false } })
            {
                continue;
            }

            var cluster = await item.Value;

            await cluster.Cluster.DisposeAsync();
            await cluster.ClusterTransactions.DisposeAsync();
        }

        _Clusters.Clear();
    }
}
