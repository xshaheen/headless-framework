// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Messaging;
using Npgsql;
using Testcontainers.PostgreSql;
using Tests.Fixtures;

namespace Tests.Stack;

[UsedImplicitly]
public sealed class PostgreSqlChaosFixture : ChaosStackFixture
{
    private readonly PostgreSqlComponent _postgreSql = new();

    public override string Database => "PostgreSQL";

    public override string StorageConnectionString => _postgreSql.ConnectionString;

    public override string ProxiedStorageConnectionString =>
        new NpgsqlConnectionStringBuilder(StorageConnectionString)
        {
            Host = StorageProxy.Host,
            Port = StorageProxy.Port,
            // A cut route refuses at once; a short connect timeout keeps a black-holed one from stalling a whole pool.
            Timeout = 5,
        }.ConnectionString;

    protected override IAsyncLifetime StorageComponent => _postgreSql;

    protected override int StorageHostPort =>
        _postgreSql.Container.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort);

    public override void UseStorage(MessagingSetupBuilder setup, string connectionString) =>
        setup.UsePostgreSql(connectionString);

    public override void UseCoordination(HeadlessCoordinationSetupBuilder setup, string connectionString) =>
        setup.UsePostgreSql(connectionString);

    private sealed class PostgreSqlComponent() : PostgreSqlStackComponent("messages_chaos");
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlChaosCollection : ICollectionFixture<PostgreSqlChaosFixture>
{
    public const string Name = "chaos-postgresql";
}
