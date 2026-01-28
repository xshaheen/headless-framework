// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase.KeyValue;
using Couchbase.Transactions.Config;
using Microsoft.Extensions.Hosting;

namespace Headless.Orm.Couchbase.Clusters;

public interface ICouchbaseTransactionConfigProvider
{
    ValueTask<TransactionConfigBuilder> GetAsync(string clusterKey);
}

public sealed class CouchbaseTransactionConfigProvider : ICouchbaseTransactionConfigProvider
{
    private readonly TransactionConfigBuilder _builder;

    public CouchbaseTransactionConfigProvider(Action<TransactionConfigBuilder>? config = null)
    {
        _builder = TransactionConfigBuilder.Create();
        config?.Invoke(_builder);
    }

    public ValueTask<TransactionConfigBuilder> GetAsync(string clusterKey) => ValueTask.FromResult(_builder);
}

public sealed class DefaultCouchbaseTransactionConfigProvider(IHostEnvironment environment)
    : ICouchbaseTransactionConfigProvider
{
    public ValueTask<TransactionConfigBuilder> GetAsync(string clusterKey)
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
