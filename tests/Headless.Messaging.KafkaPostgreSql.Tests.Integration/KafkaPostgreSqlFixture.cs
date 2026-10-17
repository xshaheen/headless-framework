// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Npgsql;
using Testcontainers.PostgreSql;
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

[CollectionDefinition("KafkaPostgreSql", DisableParallelization = true)]
public sealed class KafkaPostgreSqlCollection : ICollectionFixture<KafkaPostgreSqlFixture>;
