using System.Collections.Concurrent;
using Couchbase;
using Couchbase.Transactions;
using Framework.Arguments;
using Nito.AsyncEx;

namespace Framework.Orm.Couchbase.Clusters;

using GetClusterResult = (ICluster, Transactions);

public interface ICouchbaseClustersProvider
{
    ValueTask<GetClusterResult> GetClusterAsync(string clusterKey);
}

public sealed class CouchbaseClustersProvider(
    ICouchbaseClusterOptionsProvider clusterOptionsProvider,
    ICouchbaseTransactionConfigProvider transactionConfigProvider
) : ICouchbaseClustersProvider
{
    private readonly ConcurrentDictionary<string, AsyncLazy<GetClusterResult>> _clusters = new();

    public async ValueTask<GetClusterResult> GetClusterAsync(string clusterKey)
    {
        Argument.IsNotEmpty(clusterKey);

        return await _clusters.GetOrAdd(clusterKey, () => _CreateLazyClusterAsync(clusterKey));
    }

    private AsyncLazy<GetClusterResult> _CreateLazyClusterAsync(string clusterKey)
    {
        return new(async () =>
        {
            var clusterOptions = await clusterOptionsProvider.GetAsync(clusterKey);
            var cluster = await Cluster.ConnectAsync(clusterOptions);

            var transactionConfig = await transactionConfigProvider.GetAsync(clusterKey);
            var transactions = Transactions.Create(cluster, transactionConfig);

            return (cluster, transactions);
        });
    }
}
