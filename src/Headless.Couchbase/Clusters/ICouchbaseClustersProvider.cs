// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Couchbase;
using Couchbase.Transactions;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace Headless.Couchbase.Clusters;

/// <summary>
/// Provides lazily-created, cached Couchbase cluster and transaction instances identified by a
/// logical cluster key. Disposing this provider disposes all created clusters.
/// </summary>
[PublicAPI]
public interface ICouchbaseClustersProvider : IAsyncDisposable
{
    /// <summary>
    /// Returns (or lazily creates) the cluster and transaction manager for <paramref name="clusterKey"/>.
    /// </summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <param name="cancellationToken">
    /// A token that bounds only <em>this</em> caller's wait for the cluster. The underlying connection is a
    /// provider-shared, must-complete operation that always runs on <see cref="CancellationToken.None"/>, so
    /// cancelling this token abandons the caller's own wait without aborting — or permanently poisoning — the
    /// shared connection that other callers are awaiting. A connection attempt that fails is evicted so the
    /// next caller retries; callers that receive an already-connected cluster complete synchronously.
    /// </param>
    /// <returns>The connected cluster and its transaction manager.</returns>
    ValueTask<CouchbaseClusterConnection> GetClusterAsync(
        string clusterKey,
        CancellationToken cancellationToken = default
    );
}
