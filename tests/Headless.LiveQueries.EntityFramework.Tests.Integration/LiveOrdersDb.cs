// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>The tracked context: one table in the provider's default schema and one in a named schema.</summary>
public sealed class LiveOrdersDb(DbContextOptions<LiveOrdersDb> options) : DbContext(options)
{
    public const string OrdersKey = "table:sales.Orders";

    public DbSet<Item> Items => Set<Item>();

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>().ToTable("Items");
        modelBuilder.Entity<Order>().ToTable("Orders", "sales");
    }
}

/// <summary>A row of the default-schema table. <see cref="Test" /> keeps one test's rows apart from another's.</summary>
public sealed class Item
{
    public Guid Id { get; set; }

    public Guid Test { get; set; }

    public required string Name { get; set; }
}

/// <summary>A row of the schema-qualified table.</summary>
public sealed class Order
{
    public Guid Id { get; set; }

    public Guid Test { get; set; }

    public required string Reference { get; set; }
}
