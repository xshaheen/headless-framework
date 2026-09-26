// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Helpers;
using Headless.Testing.Testcontainers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Tests.Fixtures;

namespace Tests.Fixture;

/// <summary>
/// One PostgreSQL container hosting the shared database (schema <c>app</c>), two tenant schemas
/// (<c>tenant_a</c>, <c>tenant_b</c>), and two tenant databases (<c>tenant_da</c>, <c>tenant_db</c>).
/// </summary>
public sealed class TenantPlacementDbContextTestFixture : IAsyncLifetime
{
    public const string SchemaTenantA = "a";
    public const string SchemaTenantB = "b";
    public const string DatabaseTenantA = "da";
    public const string DatabaseTenantB = "db";
    public const string UnplacedTenant = "unplaced";
    public const int ManySchemaTenantCount = 45;

    private PostgreSqlContainer _container = null!;

    public TestCurrentTenant CurrentTenant { get; } = new();

    public string SharedConnectionString => _container.GetConnectionString();

    public ServiceProvider Services { get; private set; } = null!;

    public static string ManySchemaTenant(int index) => $"s{index:00}";

    public string TenantDatabaseConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(SharedConnectionString) { Database = database }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        _container = new PostgreSqlBuilder(TestImages.PostgreSql)
            .WithLabel("type", "tenant-placement")
            .WithDatabase("placement_shared")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await _container.StartAsync();

        Services = CreateServices(tenancy => tenancy.DataPlacement(p => p.UseConfiguration(_ConfigurePlacements)));

        await using (var scope = Services.CreateAsyncScope())
        {
            var host = scope.ServiceProvider.GetRequiredService<PlacementDbContext>();
            await host.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }

        foreach (var tenant in new[] { SchemaTenantA, SchemaTenantB })
        {
            using var _ = CurrentTenant.Change(tenant);
            await using var db = await CreateAsync<PlacementDbContext>();
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }

        foreach (var tenant in new[] { DatabaseTenantA, DatabaseTenantB })
        {
            using var _ = CurrentTenant.Change(tenant);
            await using var db = await CreateAsync<PlacementDbContext>();
            await db.Database.EnsureCreatedAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _container.DisposeAsync();
    }

    public async Task<TContext> CreateAsync<TContext>(CancellationToken cancellationToken = default)
        where TContext : HeadlessDbContext
    {
        return await Services.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContextAsync(cancellationToken);
    }

    public async Task ResetAsync()
    {
        foreach (var tenant in new[] { SchemaTenantA, SchemaTenantB, DatabaseTenantA, DatabaseTenantB })
        {
            using var _ = CurrentTenant.Change(tenant);
            await using var db = await CreateAsync<PlacementDbContext>();
            await db.Rows.IgnoreMultiTenancyFilter().ExecuteDeleteAsync();
            await db.Notes.ExecuteDeleteAsync();
        }

        CurrentTenant.Id = null;
    }

    public ServiceProvider CreateServices(Action<HeadlessTenancyBuilder> configureTenancy)
    {
        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        builder.AddHeadlessTenancy(tenancy =>
        {
            configureTenancy(tenancy);
            tenancy.EntityFramework(ef =>
                ef.GuardTenantWrites()
                    .GuardTenantReads()
                    .RouteTenantData<PlacementDbContext>()
                    .RouteTenantData<SkippingBaseConfigurationDbContext>()
                    .RouteTenantData<ExplicitTenantSchemaDbContext>()
                    .RouteTenantData<ExplicitSharedSchemaDbContext>()
                    .RouteTenantData<DataSourceDbContext>()
                    .RouteTenantData<EnumMappedDbContext>()
            );
        });
        services.AddOrReplaceSingleton<ICurrentTenant>(_ => CurrentTenant);

        services.AddHeadlessDbContext<PlacementDbContext>(o => o.UseNpgsql(SharedConnectionString));
        services.AddHeadlessDbContext<UnroutedDbContext>(o => o.UseNpgsql(SharedConnectionString));
        services.AddHeadlessDbContext<SkippingBaseConfigurationDbContext>(o => o.UseNpgsql(SharedConnectionString));
        services.AddHeadlessDbContext<ExplicitTenantSchemaDbContext>(o => o.UseNpgsql(SharedConnectionString));
        services.AddHeadlessDbContext<ExplicitSharedSchemaDbContext>(o => o.UseNpgsql(SharedConnectionString));
        services.AddSingleton(_ => NpgsqlDataSource.Create(SharedConnectionString));
        services.AddHeadlessDbContext<DataSourceDbContext>(
            (sp, o) => o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
        );
        services.AddHeadlessDbContext<EnumMappedDbContext>(o =>
            o.UseNpgsql(SharedConnectionString, npgsql => npgsql.MapEnum<PlacementMood>("placement_mood"))
        );

        return services.BuildServiceProvider();
    }

    private void _ConfigurePlacements(ConfigurationTenantDataPlacementOptions options)
    {
        options.Tenants.Add(new() { TenantId = SchemaTenantA, Schema = "tenant_a" });
        options.Tenants.Add(new() { TenantId = SchemaTenantB, Schema = "tenant_b" });
        options.Tenants.Add(
            new() { TenantId = DatabaseTenantA, ConnectionString = TenantDatabaseConnectionString("tenant_da") }
        );
        options.Tenants.Add(
            new() { TenantId = DatabaseTenantB, ConnectionString = TenantDatabaseConnectionString("tenant_db") }
        );

        for (var i = 0; i < ManySchemaTenantCount; i++)
        {
            options.Tenants.Add(new() { TenantId = ManySchemaTenant(i), Schema = $"tenant_{ManySchemaTenant(i)}" });
        }
    }
}

[CollectionDefinition(DisableParallelization = true)]
public sealed class TenantPlacementCollection : ICollectionFixture<TenantPlacementDbContextTestFixture>;

public enum PlacementMood
{
    Happy,
    Sad,
}

/// <summary>A tenant-owned row with a required tenant column.</summary>
public sealed class PlacedRow
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    public string? TenantId { get; set; }
}

/// <summary>A row that is not tenant-owned; it still lives in the tenant's schema.</summary>
public sealed class PlacedNote
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Text { get; set; }
}

public class PlacementDbContextBase(HeadlessDbContextServices services, DbContextOptions options)
    : HeadlessDbContext(services, options)
{
    public DbSet<PlacedRow> Rows => Set<PlacedRow>();

    public DbSet<PlacedNote> Notes => Set<PlacedNote>();

    public override string DefaultSchema => "app";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var row = modelBuilder.Entity<PlacedRow>();
        row.ToTable("Rows");
        row.IsTenantOwned();
        row.Property(x => x.TenantId).HasMaxLength(64);
        modelBuilder.Entity<PlacedNote>().ToTable("Notes");
    }
}

public sealed class PlacementDbContext(HeadlessDbContextServices services, DbContextOptions<PlacementDbContext> options)
    : PlacementDbContextBase(services, options);

public sealed class UnroutedDbContext(HeadlessDbContextServices services, DbContextOptions<UnroutedDbContext> options)
    : PlacementDbContextBase(services, options);

public sealed class DataSourceDbContext(
    HeadlessDbContextServices services,
    DbContextOptions<DataSourceDbContext> options
) : PlacementDbContextBase(services, options);

public sealed class EnumMappedDbContext(
    HeadlessDbContextServices services,
    DbContextOptions<EnumMappedDbContext> options
) : PlacementDbContextBase(services, options);

/// <summary>Overrides <c>OnConfiguring</c> without calling base, so routing never replaces the model cache key.</summary>
public sealed class SkippingBaseConfigurationDbContext(
    HeadlessDbContextServices services,
    DbContextOptions<SkippingBaseConfigurationDbContext> options
) : PlacementDbContextBase(services, options)
{
#pragma warning disable MA0147 // Deliberately skips base.OnConfiguring to prove the routed runtime refuses a shared model.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { }
#pragma warning restore MA0147
}

/// <summary>Maps a tenant-owned entity to an explicit schema, which a schema-placed context must refuse.</summary>
public sealed class ExplicitTenantSchemaDbContext(
    HeadlessDbContextServices services,
    DbContextOptions<ExplicitTenantSchemaDbContext> options
) : HeadlessDbContext(services, options)
{
    public override string DefaultSchema => "app";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var row = modelBuilder.Entity<PlacedRow>();
        row.ToTable("Rows", "audit");
        row.IsTenantOwned();
    }
}

/// <summary>Maps a shared (not tenant-owned) entity to an explicit schema: refused under a schema placement, kept under a database-only one.</summary>
public sealed class ExplicitSharedSchemaDbContext(
    HeadlessDbContextServices services,
    DbContextOptions<ExplicitSharedSchemaDbContext> options
) : HeadlessDbContext(services, options)
{
    public override string DefaultSchema => "app";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<PlacedNote>().ToTable("Notes", "audit");
    }
}
