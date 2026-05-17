// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using Headless.Testing.Testcontainers;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Respawn;
using Respawn.Graph;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Tests.TestSetup;

[CollectionDefinition]
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
    public async ValueTask InitializeAsync()
    {
        await _StartContainersAsync();
        await Task.WhenAll(_InitializePostgreSqlAsync(), _InitializeRedisAsync());
        await ResetAsync();
    }

    /// <summary>This runs after all the tests finished and Called before Dispose()</summary>
    public async ValueTask DisposeAsync()
    {
        await SqlConnection.DisposeAsync();
        await _postgreSqlContainer.DisposeAsync();
        await Multiplexer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    private async Task _StartContainersAsync()
    {
        await Task.WhenAll(_postgreSqlContainer.StartAsync(), _redisContainer.StartAsync());
    }

    private async Task _InitializePostgreSqlAsync()
    {
        SqlConnectionString = _postgreSqlContainer.GetConnectionString();
        SqlConnection = new NpgsqlConnection(SqlConnectionString);
        await SqlConnection.OpenAsync();
        await _RunMigrationAsync();
        Respawner = await Respawner.CreateAsync(
            SqlConnection,
            new RespawnerOptions
            {
                TablesToIgnore = [new Table(HistoryRepository.DefaultTableName)],
                DbAdapter = DbAdapter.Postgres,
            }
        );
    }

    private async Task _InitializeRedisAsync()
    {
        RedisConnectionString = _redisContainer.GetConnectionString() + ",allowAdmin=true";
        Multiplexer = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
    }

    public async Task ResetAsync()
    {
        await Task.WhenAll(Respawner.ResetAsync(SqlConnection), Multiplexer.FlushAllAsync()).WithAggregatedExceptions();
    }

    private async Task _RunMigrationAsync()
    {
        var migrationScript = await File.ReadAllTextAsync("TestSetup/postgre-init.sql");
        await using var command = new NpgsqlCommand(migrationScript, SqlConnection);
        await command.ExecuteNonQueryAsync();
    }

    private static PostgreSqlContainer _CreatePostgreSqlContainer()
    {
        return new PostgreSqlBuilder(TestImages.PostgreSql)
            .WithLabel("type", "settings")
            .WithDatabase("headless_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithReuse(true)
            .Build();
    }

    private static RedisContainer _CreateRedisContainer()
    {
        return new RedisBuilder(TestImages.Redis).WithLabel("type", "settings-redis").WithReuse(true).Build();
    }
}
