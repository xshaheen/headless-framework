// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase;

namespace Headless.Couchbase.Clusters;

/// <summary>
/// A simple <see cref="ICouchbaseClusterOptionsProvider"/> that returns the same
/// <c>ClusterOptions</c> instance for every cluster key.
/// </summary>
[PublicAPI]
public sealed class CouchbaseClusterOptionsProvider(ClusterOptions options) : ICouchbaseClusterOptionsProvider
{
    /// <inheritdoc/>
    public ValueTask<ClusterOptions> GetAsync(string clusterKey, CancellationToken cancellationToken = default)
    {
        return new(options);
    }
}
