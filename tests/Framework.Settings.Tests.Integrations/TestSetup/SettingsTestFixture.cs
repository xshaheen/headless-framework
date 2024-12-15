// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace Tests.TestSetup;

public sealed class SettingsTestFixture : IAsyncLifetime, IDisposable
{
    private readonly PostgreSqlContainer _postgreSqlContainer = _CreatePostgreSqlContainer();
    private AsyncLazy<Respawner>? _respawner;

    public string ConnectionString { get; private set; } = null!;

    /// <summary>This runs before all tests finished and Called just after the constructor</summary>
    public async Task InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();
        ConnectionString = _postgreSqlContainer.GetConnectionString();
        _respawner = new(() => _CreateRespawnerAsync(ConnectionString));
        await _RunMigrationAsync(ConnectionString);
    }

    /// <summary>This runs after all the tests run</summary>
    public void Dispose() { }

    /// <summary>This runs after all the tests finished and Called before Dispose()</summary>
    public async Task DisposeAsync()
    {
        await _postgreSqlContainer.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        Ensure.True(_respawner is not null);
        var respawner = await _respawner;
        await respawner.ResetAsync(ConnectionString);
    }

    public IServiceProvider CreateSettingsServiceProvider()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.ConfigureSettingsServices(ConnectionString);
        return serviceCollection.BuildServiceProvider();
    }

    private static Task<Respawner> _CreateRespawnerAsync(string connectionString)
    {
        return Respawner.CreateAsync(
            connectionString,
            new RespawnerOptions { TablesToIgnore = [new Table(HistoryRepository.DefaultTableName)] }
        );
    }

    private static PostgreSqlContainer _CreatePostgreSqlContainer()
    {
        return new PostgreSqlBuilder()
            .WithDatabase("SettingsTest")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithPortBinding(5432)
            .Build();
    }

    private static async Task _RunMigrationAsync(string connectionString)
    {
        var migrationScript = await File.ReadAllTextAsync("TestSetup/postgre-init.sql");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
        await using var command = new NpgsqlCommand(migrationScript, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(nameof(SettingsTestFixture))]
public sealed class SettingsTestCollection : ICollectionFixture<SettingsTestFixture>;
