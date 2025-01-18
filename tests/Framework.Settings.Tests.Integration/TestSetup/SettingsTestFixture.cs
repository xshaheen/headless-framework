// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Redis;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Respawn;
using Respawn.Graph;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Tests.TestSetup;

[CollectionDefinition(nameof(SettingsTestFixture))]
public sealed class SettingsTestFixture : ICollectionFixture<SettingsTestFixture>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgreSqlContainer = _CreatePostgreSqlContainer();
    private readonly RedisContainer _redisContainer = _CreateRedisContainer();

    public string SqlConnectionString { get; private set; } = null!;

    private NpgsqlConnection SqlConnection { get; set; } = null!;

    private Respawner Respawner { get; set; } = null!;

    public string RedisConnectionString { get; private set; } = null!;

    public ConnectionMultiplexer Multiplexer { get; private set; } = null!;

    /// <summary>This runs before all tests finished and Called just after the constructor</summary>
    public async Task InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();
        await _redisContainer.StartAsync();

        // PostgreSql
        SqlConnectionString = _postgreSqlContainer.GetConnectionString();
        SqlConnection = new NpgsqlConnection(SqlConnectionString);
        await SqlConnection.OpenAsync();
        Respawner = await Respawner.CreateAsync(
            SqlConnection,
            new RespawnerOptions
            {
                TablesToIgnore = [new Table(HistoryRepository.DefaultTableName)],
                DbAdapter = DbAdapter.Postgres,
            }
        );
        await _RunMigrationAsync();

        // Redis
        RedisConnectionString = _redisContainer.GetConnectionString() + ",allowAdmin=true";
        Multiplexer = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
    }

    /// <summary>This runs after all the tests finished and Called before Dispose()</summary>
    public async Task DisposeAsync()
    {
        await ResetAsync();
        await SqlConnection.DisposeAsync();
        await _postgreSqlContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        await Respawner.ResetAsync(SqlConnection);
        await Multiplexer.FlushAllAsync();
    }

    private async Task _RunMigrationAsync()
    {
        var migrationScript = await File.ReadAllTextAsync("TestSetup/postgre-init.sql");
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
        await using var command = new NpgsqlCommand(migrationScript, SqlConnection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    private static PostgreSqlContainer _CreatePostgreSqlContainer()
    {
        return new PostgreSqlBuilder()
            .WithDatabase("SettingsTest")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithPortBinding(5432)
            .WithReuse(true)
            .Build();
    }

    private static RedisContainer _CreateRedisContainer()
    {
        return new RedisBuilder().WithReuse(true).Build();
    }
}
