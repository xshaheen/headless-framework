// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Tests.Fixtures;

namespace Tests;

[UsedImplicitly]
public sealed class KafkaPostgreSqlFixture : MessagingStackFixtureBase
{
    private readonly BrokerComponent _kafka = new();
    private readonly PostgreSqlComponent _postgreSql = new();

    public KafkaPostgreSqlFixture()
    {
        RegisterComponents(_kafka, _postgreSql);
    }

    public string KafkaConnectionString => _kafka.ConnectionString;

    public string StorageConnectionString => _postgreSql.ConnectionString;

    public Task ResetAsync() => _postgreSql.ResetAsync();

    /// <summary>Freezes the broker, so the host's connections stay open but nothing reaches it.</summary>
    public Task PauseBrokerAsync(CancellationToken cancellationToken) => _kafka.Container.PauseAsync(cancellationToken);

    public Task UnpauseBrokerAsync(CancellationToken cancellationToken) =>
        _kafka.Container.UnpauseAsync(cancellationToken);

    private sealed class BrokerComponent : HeadlessKafkaFixture;

    private sealed class PostgreSqlComponent : PostgreSqlStackComponent;
}

[CollectionDefinition("KafkaPostgreSql", DisableParallelization = true)]
public sealed class KafkaPostgreSqlCollection : ICollectionFixture<KafkaPostgreSqlFixture>;
