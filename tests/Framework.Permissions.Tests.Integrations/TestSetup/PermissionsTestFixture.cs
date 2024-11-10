// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics;
using Framework.Kernel.BuildingBlocks.Extensions.System;
using Framework.Kernel.Checks;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace Tests.TestSetup;

public sealed class PermissionsTestFixture : IAsyncLifetime, IDisposable
{
    private readonly PostgreSqlContainer _postgreSqlContainer = _CreatePostgreSqlContainer();
    private AsyncLazy<Respawner>? _respawner;

    public string ConnectionString { get; private set; } = default!;

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

    public IServiceProvider CreateFeaturesServiceProvider()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.ConfigurePermissionsServices(ConnectionString);
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
            .WithDatabase("PermissionsTest")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithPortBinding(5432)
            .Build();
    }

    private static async Task _RunMigrationAsync(string connectionString)
    {
        var ps = new ProcessStartInfo
        {
            FileName = "./TestSetup/postgre-init.exe",
            Arguments = $"--connection \"{connectionString}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var result = await ps.RunAsTaskAsync();

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Migration failed with exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}. Output: {result.Output}"
            );
        }
    }
}

[CollectionDefinition(nameof(PermissionsTestFixture))]
public sealed class PermissionsTestCollection : ICollectionFixture<PermissionsTestFixture>;
