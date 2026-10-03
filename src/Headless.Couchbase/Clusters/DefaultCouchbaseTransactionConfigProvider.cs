// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase.KeyValue;
using Couchbase.Transactions.Config;
using Microsoft.Extensions.Hosting;

namespace Headless.Couchbase.Clusters;

/// <summary>
/// An <see cref="ICouchbaseTransactionConfigProvider"/> that builds environment-aware defaults:
/// shorter expiration in development, majority durability, and cleanup enabled.
/// </summary>
[PublicAPI]
public sealed class DefaultCouchbaseTransactionConfigProvider(IHostEnvironment environment)
    : ICouchbaseTransactionConfigProvider
{
    /// <inheritdoc/>
    public ValueTask<TransactionConfigBuilder> GetAsync(
        string clusterKey,
        CancellationToken cancellationToken = default
    )
    {
        // Note: This can provide a Default Transactions Config per cluster key
        var kvTimeout = TimeSpan.FromSeconds(10);

        var configBuilder = TransactionConfigBuilder
            .Create()
            .KeyValueTimeout(kvTimeout)
            .ExpirationTime(environment.IsDevelopment() ? kvTimeout * 50 : kvTimeout * 10)
            .DurabilityLevel(DurabilityLevel.Majority)
            .CleanupLostAttempts(cleanupLostAttempts: true)
            .CleanupClientAttempts(cleanupClientAttempts: true)
            .CleanupWindow(TimeSpan.FromSeconds(120));

        return ValueTask.FromResult(configBuilder);
    }
}
