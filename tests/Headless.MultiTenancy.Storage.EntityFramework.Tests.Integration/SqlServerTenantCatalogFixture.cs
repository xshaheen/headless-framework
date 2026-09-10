// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Testing.Testcontainers;
using Microsoft.EntityFrameworkCore;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerTenantCatalogFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerTenantCatalogFixture>,
        ITenantCatalogEfFixture
{
    private TenantCatalogEfFixtureCore _Core =>
        field ??= new TenantCatalogEfFixtureCore(
            () => ConnectionString,
            static (builder, connectionString) => builder.UseSqlServer(connectionString)
        );

    public DbContextOptions<TenantCatalogDbContext> DbOptions => _Core.DbOptions;

    public Task ResetAsync(CancellationToken cancellationToken) => _Core.ResetAsync(cancellationToken);

    public Task<ITenantStore> GetStoreAsync(CancellationToken cancellationToken) =>
        _Core.GetStoreAsync(cancellationToken);

    public Task<ITenantStore> SeedAsync(IReadOnlyList<TenantSeed> seeds, CancellationToken cancellationToken) =>
        _Core.SeedAsync(seeds, cancellationToken);
}
