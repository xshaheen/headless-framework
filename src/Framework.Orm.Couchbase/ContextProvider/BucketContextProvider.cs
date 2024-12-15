// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase;
using Framework.Orm.Couchbase.Clusters;
using Framework.Orm.Couchbase.Context;

namespace Framework.Orm.Couchbase.ContextProvider;

public interface IBucketContextProvider
{
    ValueTask<T> GetAsync<T>(string clusterKey, string bucketName, string? defaultScopeName)
        where T : CouchbaseBucketContext;
}

public sealed class BucketContextProvider(
    ICouchbaseClustersProvider couchbaseClustersProvider,
    IServiceProvider serviceProvider
) : IBucketContextProvider
{
    public async ValueTask<T> GetAsync<T>(string clusterKey, string bucketName, string? defaultScopeName)
        where T : CouchbaseBucketContext
    {
        var (cluster, transactions) = await couchbaseClustersProvider.GetClusterAsync(clusterKey);
        var bucket = await _GetBucketAsync(cluster, bucketName);

        return CouchbaseBucketContextInitializer.Initialize<T>(serviceProvider, bucket, transactions, defaultScopeName);
    }

    private static ValueTask<IBucket> _GetBucketAsync(ICluster cluster, string bucketName)
    {
        // Maybe cache this if not cached by the cluster
        return cluster.BucketAsync(bucketName);
    }
}
