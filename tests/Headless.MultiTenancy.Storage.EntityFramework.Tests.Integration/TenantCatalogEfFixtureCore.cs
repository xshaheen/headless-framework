// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// Shared implementation behind the PostgreSQL and SqlServer leaf fixtures: schema creation/reset, direct
/// <see cref="TenantRecord"/> seeding (so duplicate identifiers fail on the real unique-index violation,
/// per KTD6 — not a simulated check), and a lazily built, cached host that resolves the EF-backed
/// <see cref="ITenantStore"/> through the public <c>UseEntityFramework&lt;TContext&gt;</c> registration
/// path rather than constructing the (internal) store type directly.
/// </summary>
/// <param name="connectionString">Resolves the current container connection string on each call.</param>
/// <param name="configureProvider">Applies the leaf fixture's EF Core provider (Npgsql or SqlServer) to a builder.</param>
internal sealed class TenantCatalogEfFixtureCore(
    Func<string> connectionString,
    Action<DbContextOptionsBuilder, string> configureProvider
)
{
    private IHost? _host;
    private bool _schemaCreated;

    public DbContextOptions<TenantCatalogDbContext> DbOptions
    {
        get
        {
            if (field is null)
            {
                var builder = new DbContextOptionsBuilder<TenantCatalogDbContext>();
                configureProvider(builder, connectionString());
                field = builder.Options;
            }

            return field;
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var db = new TenantCatalogDbContext(DbOptions);

        if (!_schemaCreated)
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            _schemaCreated = true;
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync("""TRUNCATE TABLE "Tenants";""", cancellationToken);
        }
    }

    public async Task<ITenantStore> SeedAsync(IReadOnlyList<TenantSeed> seeds, CancellationToken cancellationToken)
    {
        await ResetAsync(cancellationToken);

        foreach (var seed in seeds)
        {
            // One SaveChangesAsync per seed so a duplicate normalized identifier fails precisely on the
            // colliding row (unique-index violation), leaving prior seeds committed — matching AE10's
            // "the EF store rejects the second row" wording.
            await using var db = new TenantCatalogDbContext(DbOptions);
            db.Add(_ToRecord(seed));
            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetStoreAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves the EF-backed <see cref="ITenantStore"/> without touching table data — for EF-specific
    /// scenarios that seed or update rows directly against <see cref="DbOptions"/> and then need a store
    /// to read back through.
    /// </summary>
    public async Task<ITenantStore> GetStoreAsync(CancellationToken cancellationToken)
    {
        var host = await _GetOrCreateHostAsync(cancellationToken);

        return host.Services.GetRequiredService<ITenantStore>();
    }

    private async Task<IHost> _GetOrCreateHostAsync(CancellationToken cancellationToken)
    {
        if (_host is not null)
        {
            return _host;
        }

        var connection = connectionString();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddDbContextFactory<TenantCatalogDbContext>(options => configureProvider(options, connection));
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Catalog(catalog => catalog.UseEntityFramework<TenantCatalogDbContext>())
        );

        _host = builder.Build();
        await _host.StartAsync(cancellationToken);

        return _host;
    }

    private static TenantRecord _ToRecord(TenantSeed seed)
    {
        var record = new TenantRecord(seed.Id, seed.Identifier, seed.Name, seed.IsEnabled);

        if (seed.ExtraProperties is not null)
        {
            foreach (var (key, value) in seed.ExtraProperties)
            {
                record.ExtraProperties[key] = value;
            }
        }

        return record;
    }
}
