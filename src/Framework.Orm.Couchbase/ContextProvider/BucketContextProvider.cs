// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Orm.Couchbase.Clusters;
using Framework.Orm.Couchbase.Context;

namespace Framework.Orm.Couchbase.ContextProvider;

public interface IBucketContextProvider
{
    Task<T> GetAsync<T>(string clusterKey, string bucketName, string defaultScopeName)
        where T : CouchbaseBucketContext;
}

public sealed class BucketContextProvider(
    ICouchbaseClustersProvider couchbaseClustersProvider,
    IServiceProvider serviceProvider
) : IBucketContextProvider
{
    public async Task<T> GetAsync<T>(string clusterKey, string bucketName, string? defaultScopeName)
        where T : CouchbaseBucketContext
    {
        var (cluster, transactions) = await couchbaseClustersProvider.GetClusterAsync(clusterKey);
        var bucket = await cluster.BucketAsync(bucketName);

        return BucketContextFactory.Create<T>(serviceProvider, bucket, transactions, defaultScopeName);
    }
}
