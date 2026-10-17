// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests.Stack;

[UsedImplicitly]
public sealed class PostgreSqlChaosFixture : ChaosStackFixture
{
    private readonly PostgreSqlComponent _postgreSql = new();

    public override string Database => "PostgreSQL";

    public override string StorageConnectionString => _postgreSql.Container.GetConnectionString();

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

    private sealed class PostgreSqlComponent : HeadlessPostgreSqlFixture, IAsyncLifetime
    {
        protected override PostgreSqlBuilder Configure() =>
            base.Configure().WithDatabase("messages_chaos").WithUsername("postgres").WithPassword("postgres");

        async ValueTask IAsyncLifetime.InitializeAsync()
        {
            await InitializeAsync();

            // The container is reused across runs, and the schema runner trusts the history it finds: tables an older
            // build created would be kept in their older shape. Start every run from an empty schema.
            await using var connection = new NpgsqlConnection(Container.GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """DROP SCHEMA IF EXISTS "headless" CASCADE;""";
            await command.ExecuteNonQueryAsync();
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlChaosCollection : ICollectionFixture<PostgreSqlChaosFixture>
{
    public const string Name = "chaos-postgresql";
}
