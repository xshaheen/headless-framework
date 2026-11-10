// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Tests.Fixtures;

namespace Tests;

[UsedImplicitly]
public sealed class RabbitMqPostgreSqlFixture : MessagingStackFixtureBase
{
    private readonly RabbitMqComponent _rabbitMq = new();
    private readonly PostgreSqlComponent _postgreSql = new();

    public RabbitMqPostgreSqlFixture()
    {
        RegisterComponents(_rabbitMq, _postgreSql);
    }

    public string RabbitMqHostName => _rabbitMq.HostName;

    public int RabbitMqPort => _rabbitMq.Port;

    public string RabbitMqUserName => _rabbitMq.UserName;

    public string RabbitMqPassword => _rabbitMq.Password;

    public string RabbitMqVirtualHost => _rabbitMq.VirtualHost;

    public string StorageConnectionString => _postgreSql.ConnectionString;

    public Task ResetAsync() => _postgreSql.ResetAsync();

    /// <summary>Freezes the broker, so the host's connections stay open but nothing reaches it.</summary>
    public Task PauseBrokerAsync(CancellationToken cancellationToken) =>
        _rabbitMq.Container.PauseAsync(cancellationToken);

    public Task UnpauseBrokerAsync(CancellationToken cancellationToken) =>
        _rabbitMq.Container.UnpauseAsync(cancellationToken);

    private sealed class RabbitMqComponent : HeadlessRabbitMqFixture
    {
        // Its tests pause the broker, which would freeze every other fixture on a shared one.
        protected override bool ShareContainer => false;
    }

    private sealed class PostgreSqlComponent : PostgreSqlStackComponent;
}

[CollectionDefinition("RabbitMqPostgreSql", DisableParallelization = true)]
public sealed class RabbitMqPostgreSqlCollection : ICollectionFixture<RabbitMqPostgreSqlFixture>;
