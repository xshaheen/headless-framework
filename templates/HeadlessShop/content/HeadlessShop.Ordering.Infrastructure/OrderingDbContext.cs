// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.EntityFramework.Contexts.Runtime;
using HeadlessShop.Ordering.Domain;
using Microsoft.EntityFrameworkCore;

namespace HeadlessShop.Ordering.Infrastructure;

public sealed class OrderingDbContext(HeadlessDbContextServices services, DbContextOptions<OrderingDbContext> options)
    : HeadlessDbContext(services, options)
{
    public override string? DefaultSchema => "ordering";

    public DbSet<ProductSnapshot> ProductSnapshots => Set<ProductSnapshot>();

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProductSnapshot>(entity =>
        {
            entity.ToTable("ProductSnapshots");
            entity.HasKey(product => product.Id);
            entity.Property(product => product.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(product => product.Sku).HasMaxLength(64).IsRequired();
            entity.Property(product => product.Name).HasMaxLength(160).IsRequired();
            entity.Property(product => product.Price).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(order => order.Id);
            entity.Property(order => order.TenantId).HasMaxLength(64).IsRequired();
        });
    }
}
