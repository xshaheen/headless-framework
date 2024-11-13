// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase;

namespace Framework.Orm.Couchbase.Clusters;

public interface ICouchbaseClusterOptionsProvider
{
    ValueTask<ClusterOptions> GetAsync(string clusterKey);
}

public sealed class CouchbaseClusterOptionsProvider(ClusterOptions options) : ICouchbaseClusterOptionsProvider
{
    public ValueTask<ClusterOptions> GetAsync(string clusterKey) => new(options);
}
