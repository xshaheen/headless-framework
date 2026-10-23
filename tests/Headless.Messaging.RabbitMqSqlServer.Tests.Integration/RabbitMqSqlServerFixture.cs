// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;
using Testcontainers.RabbitMq;
using Tests.Fixtures;

namespace Tests;

[UsedImplicitly]
public sealed class RabbitMqSqlServerFixture : MessagingStackFixtureBase
{
    private readonly RabbitMqComponent _rabbitMq = new();
    private readonly SqlServerComponent _sqlServer = new();

    public RabbitMqSqlServerFixture()
    {
        RegisterComponents(_rabbitMq, _sqlServer);
    }

    public string RabbitMqHostName => _rabbitMq.Container.Hostname;

    public int RabbitMqPort => _rabbitMq.Container.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);

    public string StorageConnectionString => _sqlServer.DatabaseConnectionString;

    public Task ResetAsync() => _sqlServer.ResetAsync();

    /// <summary>Freezes the broker, so the host's connections stay open but nothing reaches it.</summary>
    public Task PauseBrokerAsync(CancellationToken cancellationToken) =>
        _rabbitMq.Container.PauseAsync(cancellationToken);

    public Task UnpauseBrokerAsync(CancellationToken cancellationToken) =>
        _rabbitMq.Container.UnpauseAsync(cancellationToken);

    private sealed class RabbitMqComponent : HeadlessRabbitMqFixture;

    private sealed class SqlServerComponent : HeadlessSqlServerFixture
    {
        private const string _Database = "messages_test";

        public string DatabaseConnectionString =>
            new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = _Database }.ConnectionString;

        protected override async ValueTask InitializeAsync()
        {
            await base.InitializeAsync();

            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE DATABASE [{_Database}];
                """;
            await command.ExecuteNonQueryAsync();
        }

        public async Task ResetAsync()
        {
            try
            {
                await using var connection = new SqlConnection(DatabaseConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "TRUNCATE TABLE headless.MessagingPublished; TRUNCATE TABLE headless.MessagingReceived;";
                await command.ExecuteNonQueryAsync();
            }
            catch (SqlException)
            {
                // Schema may not exist before the first storage initializer run.
            }
        }
    }
}

[CollectionDefinition("RabbitMqSqlServer", DisableParallelization = true)]
public sealed class RabbitMqSqlServerCollection : ICollectionFixture<RabbitMqSqlServerFixture>;
