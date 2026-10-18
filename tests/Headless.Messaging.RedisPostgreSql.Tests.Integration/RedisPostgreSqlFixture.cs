// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Tests.Fixtures;

namespace Tests;

[UsedImplicitly]
public sealed class RedisPostgreSqlFixture : MessagingStackFixtureBase
{
    private readonly BrokerComponent _redis = new();
    private readonly PostgreSqlComponent _postgreSql = new();

    public RedisPostgreSqlFixture()
    {
        RegisterComponents(_redis, _postgreSql);
    }

    public string RedisConnectionString => _redis.Container.GetConnectionString();

    public string StorageConnectionString => _postgreSql.ConnectionString;

    public Task ResetAsync() => _postgreSql.ResetAsync();

    /// <summary>Freezes the broker, so the host's connections stay open but nothing reaches it.</summary>
    public Task PauseBrokerAsync(CancellationToken cancellationToken) => _redis.Container.PauseAsync(cancellationToken);

    public Task UnpauseBrokerAsync(CancellationToken cancellationToken) =>
        _redis.Container.UnpauseAsync(cancellationToken);

    private sealed class BrokerComponent : HeadlessRedisFixture;

    private sealed class PostgreSqlComponent : PostgreSqlStackComponent;
}

[CollectionDefinition("RedisPostgreSql", DisableParallelization = true)]
public sealed class RedisPostgreSqlCollection : ICollectionFixture<RedisPostgreSqlFixture>;
