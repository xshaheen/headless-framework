// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Tests.Fixtures;

namespace Tests;

[UsedImplicitly]
public sealed class PulsarPostgreSqlFixture : MessagingStackFixtureBase
{
    private readonly BrokerComponent _pulsar = new();
    private readonly PostgreSqlComponent _postgreSql = new();

    public PulsarPostgreSqlFixture()
    {
        RegisterComponents(_pulsar, _postgreSql);
    }

    public string PulsarConnectionString => _pulsar.ConnectionString;

    public string StorageConnectionString => _postgreSql.ConnectionString;

    public Task ResetAsync() => _postgreSql.ResetAsync();

    /// <summary>Freezes the broker, so the host's connections stay open but nothing reaches it.</summary>
    public Task PauseBrokerAsync(CancellationToken cancellationToken) =>
        _pulsar.Container.PauseAsync(cancellationToken);

    public Task UnpauseBrokerAsync(CancellationToken cancellationToken) =>
        _pulsar.Container.UnpauseAsync(cancellationToken);

    private sealed class BrokerComponent : HeadlessPulsarFixture;

    private sealed class PostgreSqlComponent : PostgreSqlStackComponent;
}

[CollectionDefinition("PulsarPostgreSql", DisableParallelization = true)]
public sealed class PulsarPostgreSqlCollection : ICollectionFixture<PulsarPostgreSqlFixture>;
