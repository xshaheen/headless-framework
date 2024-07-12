using Couchbase;

namespace Framework.Database.Couchbase.Clusters;

public interface ICouchbaseClusterOptionsProvider
{
    ValueTask<ClusterOptions> GetAsync(string clusterKey);
}

public sealed class CouchbaseClusterOptionsProvider(ClusterOptions options) : ICouchbaseClusterOptionsProvider
{
    public ValueTask<ClusterOptions> GetAsync(string clusterKey) => new(options);
}
