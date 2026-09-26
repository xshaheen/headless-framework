// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Helpers;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Tests.Fixture;

namespace Tests.Tenancy;

public sealed class TenantMigrationFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public string SharedConnectionString => _container.GetConnectionString();

    public string DatabaseConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(SharedConnectionString) { Database = database }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        _container = new PostgreSqlBuilder(TestImages.PostgreSql)
            .WithLabel("type", "tenant-migrations")
            .WithDatabase("migrations_shared")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await _container.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(DisableParallelization = true)]
public sealed class TenantMigrationCollection : ICollectionFixture<TenantMigrationFixture>;

[Collection<TenantMigrationCollection>]
public sealed class TenantDataPlacementMigrationTests(TenantMigrationFixture fixture) : TestBase
{
    [Fact]
    public async Task should_migrate_every_tenant_into_its_own_schema_or_database()
    {
        // given: two schema tenants (one disabled) and one tenant with its own database and schema
        await using var services = _CreateServices(
            new("m1", "tenant_m1", Database: null, IsEnabled: true),
            new MigrationTenant("m2", "tenant_m2", Database: null, IsEnabled: false),
            new MigrationTenant("md", "tenant_md", Database: "tenant_md", IsEnabled: true)
        );

        // when
        await services.MigrateTenantDatabasesAsync<TenantMigrationDbContext>(AbortToken);

        // then
        foreach (
            var (connectionString, schema) in new[]
            {
                (fixture.SharedConnectionString, "tenant_m1"),
                (fixture.SharedConnectionString, "tenant_m2"),
                (fixture.DatabaseConnectionString("tenant_md"), "tenant_md"),
            }
        )
        {
            (await _TablesAsync(connectionString, schema))
                .Should()
                .BeEquivalentTo("Rows", "Notes", "__EFMigrationsHistory");
            (
                await _ScalarAsync<long>(
                    connectionString,
                    $"""SELECT count(*) FROM "{schema}"."__EFMigrationsHistory" """
                )
            )
                .Should()
                .Be(2);
            (await _ScalarAsync<string>(connectionString, $"""SELECT "Name" FROM "{schema}"."Rows" """))
                .Should()
                .Be("seeded");
            (
                await _ScalarAsync<int>(
                    connectionString,
                    $"""
                    SELECT character_maximum_length FROM information_schema.columns
                    WHERE table_schema = '{schema}' AND table_name = 'Rows' AND column_name = 'Name'
                    """
                )
            ).Should().Be(200);
        }

        (await _TablesAsync(fixture.SharedConnectionString, "app")).Should().BeEmpty();
        (await _SchemaExistsAsync(fixture.DatabaseConnectionString("tenant_md"), "app")).Should().BeFalse();
    }

    [Fact]
    public async Task should_be_idempotent()
    {
        // given
        await using var services = _CreateServices(
            new MigrationTenant("idem", "tenant_idem", Database: null, IsEnabled: true)
        );
        await services.MigrateTenantDatabasesAsync<TenantMigrationDbContext>(AbortToken);

        // when
        await services.MigrateTenantDatabasesAsync<TenantMigrationDbContext>(AbortToken);

        // then
        (
            await _ScalarAsync<long>(
                fixture.SharedConnectionString,
                """SELECT count(*) FROM "tenant_idem"."__EFMigrationsHistory" """
            )
        )
            .Should()
            .Be(2);
    }

    [Fact]
    public async Task should_migrate_the_host_placement_with_the_existing_host_runner()
    {
        // given: no ambient tenant, so the routed context keeps its registration placement and EF's own
        // pending-model-changes check runs against the unrewritten snapshot
        await using var services = _CreateServices(
            new MigrationTenant("hosted", "tenant_hosted", Database: null, IsEnabled: true)
        );

        // when
        await services.MigrateDbContextByFactoryAsync<TenantMigrationDbContext>(AbortToken);

        // then
        (await _TablesAsync(fixture.SharedConnectionString, "app"))
            .Should()
            .BeEquivalentTo("Rows", "Notes");
        (await _SchemaExistsAsync(fixture.SharedConnectionString, "tenant_hosted")).Should().BeFalse();

        await using var connection = new NpgsqlConnection(fixture.SharedConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var cleanup = connection.CreateCommand();
        cleanup.CommandText = """DROP SCHEMA "app" CASCADE; DROP TABLE IF EXISTS public."__EFMigrationsHistory";""";
        await cleanup.ExecuteNonQueryAsync(AbortToken);
    }

    [Fact]
    public async Task should_stop_at_the_first_failing_tenant_and_propagate_its_exception()
    {
        // given: the first tenant has no placement, so its routed context is refused
        await using var services = _CreateServicesFor(
            [new("ghost", Schema: null, Database: null, IsEnabled: true), new("late", "tenant_late", null, true)],
            withDirectory: true
        );

        // when
        var act = () => services.MigrateTenantDatabasesAsync<TenantMigrationDbContext>(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'ghost' has no data placement*");
        (await _SchemaExistsAsync(fixture.SharedConnectionString, "tenant_late")).Should().BeFalse();
    }

    [Fact]
    public async Task should_require_a_tenant_directory()
    {
        await using var services = _CreateServicesFor([new("m1", "tenant_m1", null, true)], withDirectory: false);

        var act = () => services.MigrateTenantDatabasesAsync<TenantMigrationDbContext>(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ITenantDirectory*");
    }

    [Fact]
    public async Task should_propagate_cancellation()
    {
        await using var services = _CreateServices(new MigrationTenant("cancelled", "tenant_cancelled", null, true));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => services.MigrateTenantDatabasesAsync<TenantMigrationDbContext>(cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private ServiceProvider _CreateServices(params MigrationTenant[] tenants) =>
        _CreateServicesFor(tenants, withDirectory: true);

    private ServiceProvider _CreateServicesFor(MigrationTenant[] tenants, bool withDirectory)
    {
        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        builder.AddHeadlessTenancy(tenancy =>
            tenancy
                .DataPlacement(p =>
                    p.UseConfiguration(options =>
                    {
                        foreach (var tenant in tenants.Where(t => t.Schema is not null || t.Database is not null))
                        {
                            options.Tenants.Add(
                                new()
                                {
                                    TenantId = tenant.Id,
                                    Schema = tenant.Schema,
                                    ConnectionString = tenant.Database is null
                                        ? null
                                        : fixture.DatabaseConnectionString(tenant.Database),
                                }
                            );
                        }
                    })
                )
                .EntityFramework(ef => ef.GuardTenantWrites().RouteTenantData<TenantMigrationDbContext>())
        );
        services.AddOrReplaceSingleton<ICurrentTenant>(_ => new TestCurrentTenant());

        if (withDirectory)
        {
            services.AddSingleton<ITenantDirectory>(
                new StaticTenantDirectory([.. tenants.Select(t => new TenantInfo(t.Id, t.Id, t.Id, t.IsEnabled))])
            );
        }

        services.AddHeadlessDbContext<TenantMigrationDbContext>(o => o.UseNpgsql(fixture.SharedConnectionString));

        return services.BuildServiceProvider();
    }

    private async Task<List<string>> _TablesAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema";
        command.Parameters.AddWithValue(nameof(schema), schema);
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        var tables = new List<string>();

        while (await reader.ReadAsync(AbortToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private async Task<bool> _SchemaExistsAsync(string connectionString, string schema)
    {
        return await _ScalarAsync<long>(
                connectionString,
                $"SELECT count(*) FROM information_schema.schemata WHERE schema_name = '{schema}'"
            ) > 0;
    }

    private async Task<T> _ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return (T)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private sealed record MigrationTenant(string Id, string? Schema, string? Database, bool IsEnabled);

    private sealed class StaticTenantDirectory(IReadOnlyList<TenantInfo> tenants) : ITenantDirectory
    {
        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(tenants);
    }
}
