// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.EntityFramework.Contexts.Runtime;
using HeadlessShop.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace HeadlessShop.Catalog.Infrastructure;

public sealed class CatalogDbContext(HeadlessDbContextServices services, DbContextOptions<CatalogDbContext> options)
    : HeadlessDbContext(services, options)
{
    public override string? DefaultSchema => "catalog";

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(product => product.Id);
            entity.HasIndex(product => new { product.TenantId, product.Sku }).IsUnique();
            entity.Property(product => product.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(product => product.Sku).HasMaxLength(64).IsRequired();
            entity.Property(product => product.Name).HasMaxLength(160).IsRequired();
            entity.Property(product => product.Price).HasColumnType("decimal(18,2)");
        });
    }
}
