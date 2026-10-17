// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;

namespace Tests.Stack;

[UsedImplicitly]
public sealed class SqlServerChaosFixture : ChaosStackFixture
{
    private readonly SqlServerComponent _sqlServer = new();

    public override string Database => "SQL Server";

    public override string StorageConnectionString => _sqlServer.DatabaseConnectionString;

    public override string ProxiedStorageConnectionString =>
        new SqlConnectionStringBuilder(StorageConnectionString)
        {
            DataSource = $"{StorageProxy.Host},{StorageProxy.Port}",
            // A cut route refuses at once; a short connect timeout keeps a black-holed one from stalling a whole pool.
            ConnectTimeout = 5,
        }.ConnectionString;

    protected override IAsyncLifetime StorageComponent => _sqlServer;

    protected override int StorageHostPort =>
        int.Parse(
            new SqlConnectionStringBuilder(_sqlServer.ConnectionString).DataSource.Split(',')[1],
            CultureInfo.InvariantCulture
        );

    public override void UseStorage(MessagingSetupBuilder setup, string connectionString) =>
        setup.UseSqlServer(connectionString);

    public override void UseCoordination(HeadlessCoordinationSetupBuilder setup, string connectionString) =>
        setup.UseSqlServer(connectionString);

    private sealed class SqlServerComponent : HeadlessSqlServerFixture
    {
        private const string _Database = "messages_chaos";

        public string DatabaseConnectionString =>
            new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = _Database }.ConnectionString;

        protected override async ValueTask InitializeAsync()
        {
            await base.InitializeAsync();

            // The container is reused across runs, and the schema runner trusts the history it finds: tables an older
            // build created would be kept in their older shape. Start every run from a fresh database.
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{_Database}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{_Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_Database}];
                END;
                CREATE DATABASE [{_Database}];
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerChaosCollection : ICollectionFixture<SqlServerChaosFixture>
{
    public const string Name = "chaos-sqlserver";
}
