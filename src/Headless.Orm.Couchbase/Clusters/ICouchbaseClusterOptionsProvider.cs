// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase;

namespace Headless.Couchbase.Clusters;

/// <summary>
/// Resolves Couchbase <c>ClusterOptions</c> for a named cluster key. Implement this interface to
/// supply different connection strings or authentication credentials per logical cluster.
/// </summary>
[PublicAPI]
public interface ICouchbaseClusterOptionsProvider
{
    /// <summary>Returns the <c>ClusterOptions</c> to use when connecting to the cluster identified by <paramref name="clusterKey"/>.</summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>The Couchbase cluster options.</returns>
    ValueTask<ClusterOptions> GetAsync(string clusterKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// A simple <see cref="ICouchbaseClusterOptionsProvider"/> that returns the same
/// <c>ClusterOptions</c> instance for every cluster key.
/// </summary>
[PublicAPI]
public sealed class CouchbaseClusterOptionsProvider(ClusterOptions options) : ICouchbaseClusterOptionsProvider
{
    /// <inheritdoc/>
    public ValueTask<ClusterOptions> GetAsync(string clusterKey, CancellationToken cancellationToken = default) =>
        new(options);
}
