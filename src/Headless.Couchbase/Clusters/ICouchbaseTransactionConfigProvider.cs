// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase.Client.Transactions.Config;
using Couchbase.KeyValue;
using Microsoft.Extensions.Hosting;

namespace Headless.Couchbase.Clusters;

/// <summary>
/// Resolves a Couchbase <c>TransactionsConfigBuilder</c> for a named cluster key. Implement this
/// interface to provide per-cluster transaction settings (durability, timeout, cleanup).
/// </summary>
[PublicAPI]
public interface ICouchbaseTransactionConfigProvider
{
    /// <summary>Returns the transaction config builder for the cluster identified by <paramref name="clusterKey"/>.</summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A configured <c>TransactionsConfigBuilder</c>.</returns>
    ValueTask<TransactionsConfigBuilder> GetAsync(string clusterKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// An <see cref="ICouchbaseTransactionConfigProvider"/> that returns the same
/// <c>TransactionsConfigBuilder</c> for every cluster key, optionally configured via a callback.
/// </summary>
[PublicAPI]
public sealed class CouchbaseTransactionConfigProvider : ICouchbaseTransactionConfigProvider
{
    private readonly TransactionsConfigBuilder _builder;

    /// <summary>
    /// Initializes the provider with an optional configuration callback applied to a new builder.
    /// </summary>
    /// <param name="config">Optional callback to customize the transaction config.</param>
    public CouchbaseTransactionConfigProvider(Action<TransactionsConfigBuilder>? config = null)
    {
        _builder = TransactionsConfigBuilder.Create();
        config?.Invoke(_builder);
    }

    /// <inheritdoc/>
    public ValueTask<TransactionsConfigBuilder> GetAsync(
        string clusterKey,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(_builder);
    }
}

/// <summary>
/// An <see cref="ICouchbaseTransactionConfigProvider"/> that builds environment-aware defaults:
/// shorter expiration in development, majority durability, and cleanup enabled.
/// </summary>
[PublicAPI]
public sealed class DefaultCouchbaseTransactionConfigProvider(IHostEnvironment environment)
    : ICouchbaseTransactionConfigProvider
{
    /// <inheritdoc/>
    public ValueTask<TransactionsConfigBuilder> GetAsync(
        string clusterKey,
        CancellationToken cancellationToken = default
    )
    {
        // Note: This can provide a Default Transactions Config per cluster key
        var kvTimeout = TimeSpan.FromSeconds(10);

        var configBuilder = TransactionsConfigBuilder
            .Create()
            .KeyValueTimeout(kvTimeout)
            .ExpirationTime(environment.IsDevelopment() ? kvTimeout * 50 : kvTimeout * 10)
            .DurabilityLevel(DurabilityLevel.Majority)
            .CleanupConfig(
                TransactionCleanupConfigBuilder
                    .Create()
                    .CleanupLostAttempts(cleanupLostAttempts: true)
                    .CleanupClientAttempts(cleanupClientAttempts: true)
                    .CleanupWindow(TimeSpan.FromSeconds(120))
                    .Build()
            );

        return ValueTask.FromResult(configBuilder);
    }
}
