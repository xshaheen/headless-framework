// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase;

namespace Headless.Couchbase.Clusters;

/// <summary>
/// Resolves Couchbase <c>ClusterOptions</c> for a named cluster key. Implement this interface to
/// supply different connection strings or authentication credentials per logical cluster.
/// </summary>
public interface ICouchbaseClusterOptionsProvider
{
    /// <summary>Returns the <c>ClusterOptions</c> to use when connecting to the cluster identified by <paramref name="clusterKey"/>.</summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <returns>The Couchbase cluster options.</returns>
    ValueTask<ClusterOptions> GetAsync(string clusterKey);
}

/// <summary>
/// A simple <see cref="ICouchbaseClusterOptionsProvider"/> that returns the same
/// <c>ClusterOptions</c> instance for every cluster key.
/// </summary>
public sealed class CouchbaseClusterOptionsProvider(ClusterOptions options) : ICouchbaseClusterOptionsProvider
{
    /// <inheritdoc/>
    public ValueTask<ClusterOptions> GetAsync(string clusterKey) => new(options);
}
