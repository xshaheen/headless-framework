// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Headless.Abstractions;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Helpers;
using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Tests.Fixtures;

namespace Tests.Tenancy;

public enum TenantDatabaseProvider
{
    PostgreSql,
    SqlServer,
}

public abstract class TenantDatabaseFixture(TenantDatabaseProvider provider) : IAsyncLifetime
{
    private IContainer? _container;
    private string _connectionString = null!;

    public TenantDatabaseProvider Provider { get; } = provider;
    public TestCurrentTenant CurrentTenant { get; } = new();
    public ServiceProvider Services { get; private set; } = null!;
    public string MigrationSql { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        if (Provider == TenantDatabaseProvider.PostgreSql)
        {
            var container = new PostgreSqlBuilder(TestImages.PostgreSql)
                .WithDatabase("tenant_conformance")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .WithLabel("type", "tenant-conformance")
                .Build();
            _container = container;
            await container.StartAsync(timeout.Token);
            _connectionString = container.GetConnectionString();
        }
        else
        {
            // Azure SQL Edge does not prove SQL Server 2022 behavior on ARM hosts.
            var container = new MsSqlBuilder(new DockerImage(TestImages.MsSqlServer, new Platform("linux/amd64")))
                .WithLabel("type", "tenant-conformance")
                .Build();
            _container = container;
            await container.StartAsync(timeout.Token);
            var connectionString = new SqlConnectionStringBuilder(container.GetConnectionString());
            await using (var connection = new SqlConnection(connectionString.ConnectionString))
            {
                await connection.OpenAsync(timeout.Token);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT CONVERT(int, SERVERPROPERTY('ProductMajorVersion'))";
                ((int)(await command.ExecuteScalarAsync(timeout.Token))!).Should().Be(16);
            }
            connectionString.InitialCatalog = "tenant_conformance";
            _connectionString = connectionString.ConnectionString;
        }

        Services = CreateServices(ConfigureServices);
        await using var scope = Services.CreateAsyncScope();
        var db = GetContext(scope.ServiceProvider);
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(timeout.Token))
        {
            await creator.CreateAsync(timeout.Token);
        }
        var model = db.GetService<IDesignTimeModel>().Model;
        var operations = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(operations, model);
        MigrationSql = string.Join(Environment.NewLine, commands.Select(x => x.CommandText));
        foreach (var command in commands)
        {
            await db.Database.ExecuteSqlRawAsync(command.CommandText, timeout.Token);
        }
    }

    public void ConfigureOptions(DbContextOptionsBuilder options)
    {
        if (Provider == TenantDatabaseProvider.PostgreSql)
        {
            options.UseNpgsql(_connectionString);
        }
        else
        {
            options.UseSqlServer(_connectionString);
        }
        options.AddHeadlessExtension();
    }

    public ServiceProvider CreateServices(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        services.AddHeadlessTenantWriteGuard();
        services.AddSingleton<ICurrentTenant>(CurrentTenant);
        services.AddRecordingHeadlessDispatcher();
        configure(services);
        return services.BuildServiceProvider();
    }

    protected abstract void ConfigureServices(IServiceCollection services);
    protected abstract DbContext GetContext(IServiceProvider services);

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        CurrentTenant.Id = "reset";
        await using var scope = Services.CreateAsyncScope();
        var db = GetContext(scope.ServiceProvider);
        var helper = db.GetService<ISqlGenerationHelper>();
        var tables = db.Model.GetRelationalModel().Tables.ToList();
        while (tables.Count != 0)
        {
            var leaves = tables
                .Where(t =>
                    !tables.Exists(other => other != t && other.ForeignKeyConstraints.Any(fk => fk.PrincipalTable == t))
                )
                .ToArray();
            leaves.Should().NotBeEmpty("the conformance model has no FK cycles");
            foreach (var table in leaves)
            {
                var deleteSql = $"DELETE FROM {helper.DelimitIdentifier(table.Name, table.Schema)}";
                await db.Database.ExecuteSqlRawAsync(deleteSql, cancellationToken);
                tables.Remove(table);
            }
        }
        CurrentTenant.Id = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Services is not null)
        {
            await Services.DisposeAsync();
        }
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
}
