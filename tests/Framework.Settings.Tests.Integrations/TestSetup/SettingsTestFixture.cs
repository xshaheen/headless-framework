// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Docker.DotNet.Models;
using DotNet.Testcontainers.Containers;
using Framework.Caching;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.ResourceLocks.Local;
using Framework.Settings;
using Framework.Settings.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nito.AsyncEx;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace Tests.TestSetup;

public sealed class SettingsTestFixture : IAsyncLifetime, IDisposable
{
    private readonly PostgreSqlContainer _postgreSqlContainer = _CreatePostgreSqlContainer();
    private readonly string _connectionString;
    private readonly AsyncLazy<Respawner> _respawner;

    public IHostEnvironment Environment { get; }

    public IServiceScopeFactory ScopeFactory { get; }

    public SettingsTestFixture()
    {
        _connectionString = _postgreSqlContainer.GetConnectionString();
        _respawner = new(() => _CreateRespawnerAsync(_connectionString));

        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IUniqueLongGenerator, SnowFlakIdUniqueLongGenerator>();
        builder.Services.AddSingleton<IGuidGenerator, SequentialAsStringGuidGenerator>();
        builder.Services.AddSingleton(Substitute.For<ICurrentUser>());
        builder.Services.AddSingleton(Substitute.For<ICurrentTenant>());
        builder.Services.AddSingleton(Substitute.For<IApplicationInformationAccessor>());
        builder.Services.AddInMemoryCache();
        builder.Services.AddLocalResourceLock();

        builder
            .Services.AddSettingsManagementCore()
            .AddSettingsManagementEntityFrameworkStorage(options =>
            {
                options.UseNpgsql(_connectionString);
            });

        var host = builder.Build();

        Environment = builder.Environment;
        ScopeFactory = host.Services.GetRequiredService<IServiceScopeFactory>();
    }

    /// <summary>This runs before all tests finished and Called just after the constructor</summary>
    public async Task InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();
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
        var respawner = await _respawner;
        await respawner.ResetAsync(_connectionString);
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
}
