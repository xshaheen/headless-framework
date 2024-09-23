// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Couchbase.Transactions.Config;

namespace Framework.Orm.Couchbase.Clusters;

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
