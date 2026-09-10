// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>
/// Minimal test <see cref="DbContext"/> wiring only the tenant catalog entity — the EF store (resolved
/// through <c>UseEntityFramework&lt;TContext&gt;</c>) constrains its context type parameter to plain
/// <see cref="DbContext"/>, not <c>HeadlessDbContext</c>, so this is representative of what a real
/// consumer wires.
/// </summary>
public sealed class TenantCatalogDbContext(DbContextOptions<TenantCatalogDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddHeadlessTenancyCatalog(this);
    }
}
