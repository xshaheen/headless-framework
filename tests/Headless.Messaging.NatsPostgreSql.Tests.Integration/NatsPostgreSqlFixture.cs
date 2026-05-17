// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Npgsql;
using Testcontainers.Nats;
using Testcontainers.PostgreSql;
using Tests.Fixtures;

namespace Tests;

[UsedImplicitly]
public sealed class NatsPostgreSqlFixture : MessagingStackFixtureBase
{
    private static readonly byte[] _NatsConfig = Encoding.UTF8.GetBytes(
        """
        port: 4222
        monitor_port: 8222
        jetstream {}
        """
    );

    private readonly NatsStackComponent _nats;
    private readonly PostgreSqlStackComponent _postgreSql;

    public NatsPostgreSqlFixture()
    {
        _nats = new NatsStackComponent();
        _postgreSql = new PostgreSqlStackComponent();
        RegisterComponents(_nats, _postgreSql);
    }

    public string NatsConnectionString => _nats.ConnectionString;

    public string PostgreSqlConnectionString => _postgreSql.ConnectionString;

    public Task EnsureStreamAsync(string streamName, string subjectWildcard) =>
        _nats.EnsureStreamAsync(streamName, subjectWildcard);

    public Task ResetAsync() => _postgreSql.ResetAsync();

    private sealed class NatsStackComponent : IAsyncLifetime
    {
        private readonly NatsContainer _container = new NatsBuilder(TestImages.Nats)
            .WithLabel("type", "nats-postgresql-nats")
            .WithResourceMapping(_NatsConfig, "/etc/nats/nats-server.conf")
            .WithReuse(true)
            .Build();

        private NatsConnection? _connection;

        public string ConnectionString => _container.GetConnectionString();

        public async ValueTask InitializeAsync()
        {
            await _container.StartAsync();
            await GetConnectionAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            await _container.DisposeAsync();
        }

        public async Task EnsureStreamAsync(string streamName, string subjectWildcard)
        {
            var js = new NatsJSContext(await GetConnectionAsync());

            try
            {
                await js.CreateOrUpdateStreamAsync(
                    new StreamConfig
                    {
                        Name = streamName,
                        Subjects = [subjectWildcard],
                        Storage = StreamConfigStorage.Memory,
                    }
                );
            }
            catch (NatsJSApiException e) when (e.Error.Code == 409)
            {
                // Already exists.
            }
        }

        private async Task<NatsConnection> GetConnectionAsync()
        {
            if (_connection is not null)
            {
                return _connection;
            }

            var options = NatsOpts.Default with { Url = ConnectionString, ConnectTimeout = TimeSpan.FromSeconds(30) };

            _connection = new NatsConnection(options);
            await _connection.ConnectAsync();
            return _connection;
        }
    }

    private sealed class PostgreSqlStackComponent : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(TestImages.PostgreSql)
            .WithLabel("type", "nats-postgresql-pg")
            .WithDatabase("messages_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithReuse(true)
            .Build();

        public string ConnectionString => _container.GetConnectionString();

        public async ValueTask InitializeAsync()
        {
            await _container.StartAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _container.DisposeAsync();
        }

        public async Task ResetAsync()
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText = """
                    TRUNCATE TABLE messaging.published;
                    TRUNCATE TABLE messaging.received;
                    UPDATE messaging.lock SET "Instance" = '', "LastLockTime" = '0001-01-01 00:00:00';
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

[CollectionDefinition("NatsPostgreSql", DisableParallelization = true)]
public sealed class NatsPostgreSqlCollection : ICollectionFixture<NatsPostgreSqlFixture>;
