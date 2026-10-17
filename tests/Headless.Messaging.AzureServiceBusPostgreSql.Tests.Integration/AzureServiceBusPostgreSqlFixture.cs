// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Npgsql;
using Testcontainers.PostgreSql;
using Tests.Fixtures;

namespace Tests;

[UsedImplicitly]
public sealed class AzureServiceBusPostgreSqlFixture : MessagingStackFixtureBase
{
    public const string ConnectionStringEnvironmentVariable = "HEADLESS_TEST_AZURE_SERVICE_BUS_CONNECTION_STRING";

    private readonly string? _serviceBusConnectionString = Environment.GetEnvironmentVariable(
        ConnectionStringEnvironmentVariable
    );

    private readonly PostgreSqlComponent _postgreSql = new();

    public AzureServiceBusPostgreSqlFixture()
    {
        RegisterComponents(_postgreSql);
    }

    /// <summary>Returns the namespace credential, or skips the calling test when none was provided.</summary>
    public string RequireServiceBusConnectionString()
    {
        if (string.IsNullOrWhiteSpace(_serviceBusConnectionString))
        {
            Assert.Skip(
                $"Azure Service Bus real-service tests require {ConnectionStringEnvironmentVariable}; no namespace credential was provided."
            );
        }

        return _serviceBusConnectionString!;
    }

    public string StorageConnectionString => _postgreSql.ConnectionString;

    public Task ResetAsync() => _postgreSql.ResetAsync();

    private sealed class PostgreSqlComponent : HeadlessPostgreSqlFixture, IAsyncLifetime
    {
        public string ConnectionString => Container.GetConnectionString();

        protected override PostgreSqlBuilder Configure()
        {
            return base.Configure().WithDatabase("messages_test").WithUsername("postgres").WithPassword("postgres");
        }

        async ValueTask IAsyncLifetime.InitializeAsync()
        {
            await InitializeAsync();

            // The container is reused across runs, and the schema runner trusts the history it finds: tables an older
            // build created would be kept in their older shape. Start every run from an empty messaging schema.
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """DROP SCHEMA IF EXISTS "headless" CASCADE;""";
            await command.ExecuteNonQueryAsync();
        }

        public async Task ResetAsync()
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    TRUNCATE TABLE headless.messaging_published;
                    TRUNCATE TABLE headless.messaging_received;
                    """;
                await command.ExecuteNonQueryAsync();
            }
            catch (PostgresException)
            {
                // Schema may not exist before the first storage initializer run.
            }
        }
    }
}

[CollectionDefinition("AzureServiceBusPostgreSql", DisableParallelization = true)]
public sealed class AzureServiceBusPostgreSqlCollection : ICollectionFixture<AzureServiceBusPostgreSqlFixture>;
