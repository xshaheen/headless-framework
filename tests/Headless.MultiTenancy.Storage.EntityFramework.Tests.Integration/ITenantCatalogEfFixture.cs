// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>
/// Extends <see cref="ITenantCatalogStoreFixture"/> with direct <see cref="TenantCatalogDbContext"/> access
/// so EF-only scenarios (collation proof, the identifier-update path) can insert and update rows below the
/// provider-neutral <see cref="ITenantCatalogStoreFixture.SeedAsync"/> seam, which only ever writes
/// already-normalized identifiers.
/// </summary>
public interface ITenantCatalogEfFixture : ITenantCatalogStoreFixture
{
    /// <summary>EF Core options pointed at this fixture's container database and the shared <c>Tenants</c> table.</summary>
    DbContextOptions<TenantCatalogDbContext> DbOptions { get; }

    /// <summary>Clears every row from the <c>Tenants</c> table, creating the schema first if it does not exist yet.</summary>
    Task ResetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the EF-backed <see cref="ITenantStore"/> without touching table data — used after seeding
    /// or updating rows directly through <see cref="DbOptions"/>.
    /// </summary>
    Task<ITenantStore> GetStoreAsync(CancellationToken cancellationToken);
}
