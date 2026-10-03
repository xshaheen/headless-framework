// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase.KeyValue;
using Couchbase.Transactions.Config;
using Microsoft.Extensions.Hosting;

namespace Headless.Couchbase.Clusters;

/// <summary>
/// Resolves a Couchbase <c>TransactionConfigBuilder</c> for a named cluster key. Implement this
/// interface to provide per-cluster transaction settings (durability, timeout, cleanup).
/// </summary>
[PublicAPI]
public interface ICouchbaseTransactionConfigProvider
{
    /// <summary>Returns the transaction config builder for the cluster identified by <paramref name="clusterKey"/>.</summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A configured <c>TransactionConfigBuilder</c>.</returns>
    ValueTask<TransactionConfigBuilder> GetAsync(string clusterKey, CancellationToken cancellationToken = default);
}
