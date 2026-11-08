// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;
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

    public string RabbitMqHostName => _rabbitMq.HostName;

    public int RabbitMqPort => _rabbitMq.Port;

    public string RabbitMqUserName => _rabbitMq.UserName;

    public string RabbitMqPassword => _rabbitMq.Password;

    public string RabbitMqVirtualHost => _rabbitMq.VirtualHost;

    public string StorageConnectionString => _sqlServer.ConnectionString;

    public Task ResetAsync() => _sqlServer.ResetAsync();

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

    private sealed class SqlServerComponent : HeadlessSqlServerFixture
    {
        public async Task ResetAsync()
        {
            try
            {
                await using var connection = new SqlConnection(ConnectionString);
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
