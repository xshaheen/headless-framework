// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase.KeyValue;
using Couchbase.Transactions.Config;
using Microsoft.Extensions.Hosting;

namespace Headless.Couchbase.Clusters;

/// <summary>
/// An <see cref="ICouchbaseTransactionConfigProvider"/> that returns the same
/// <c>TransactionConfigBuilder</c> for every cluster key, optionally configured via a callback.
/// </summary>
[PublicAPI]
public sealed class CouchbaseTransactionConfigProvider : ICouchbaseTransactionConfigProvider
{
    private readonly TransactionConfigBuilder _builder;

    /// <summary>
    /// Initializes the provider with an optional configuration callback applied to a new builder.
    /// </summary>
    /// <param name="config">Optional callback to customize the transaction config.</param>
    public CouchbaseTransactionConfigProvider(Action<TransactionConfigBuilder>? config = null)
    {
        _builder = TransactionConfigBuilder.Create();
        config?.Invoke(_builder);
    }

    /// <inheritdoc/>
    public ValueTask<TransactionConfigBuilder> GetAsync(
        string clusterKey,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(_builder);
    }
}
