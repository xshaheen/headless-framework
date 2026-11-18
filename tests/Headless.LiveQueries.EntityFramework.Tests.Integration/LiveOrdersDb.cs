// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests;

/// <summary>
/// The tracked context: one table in the provider's default schema, one in a named schema, and two whose rows refer
/// to an order, so the database removes or clears them when the order is deleted.
/// </summary>
public sealed class LiveOrdersDb(DbContextOptions<LiveOrdersDb> options) : DbContext(options)
{
    public const string OrdersKey = "table:sales.Orders";

    public DbSet<Item> Items => Set<Item>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public DbSet<Shipment> Shipments => Set<Shipment>();

    public DbSet<Parcel> Parcels => Set<Parcel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>().ToTable("Items");
        modelBuilder.Entity<Order>().ToTable("Orders", "sales");

        modelBuilder.Entity<OrderLine>(line =>
        {
            line.ToTable("OrderLines", "sales");
            line.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Shipment>(shipment =>
        {
            shipment.ToTable("Shipments", "sales");
            shipment.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Parcel>(parcel =>
        {
            parcel.ToTable("Parcels", "sales");

            // Fixed when the row is inserted, which is what lets a filter on them be matched against a saved row.
            parcel.Property(x => x.Test).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            parcel.Property(x => x.OwnerId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        });
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

/// <summary>A row the database deletes with its order.</summary>
public sealed class OrderLine
{
    public Guid Id { get; set; }

    public Guid Test { get; set; }

    public Guid OrderId { get; set; }
}

/// <summary>A row the database detaches from its order, by clearing the column, when the order is deleted.</summary>
public sealed class Shipment
{
    public Guid Id { get; set; }

    public Guid Test { get; set; }

    public Guid? OrderId { get; set; }
}

/// <summary>
/// The row of the row-matching scenarios: a key the database generates, two columns fixed at insert, and one that
/// stays changeable.
/// </summary>
public sealed class Parcel
{
    public int Id { get; set; }

    public Guid Test { get; set; }

    public int OwnerId { get; set; }

    public int Weight { get; set; }

    public required string Label { get; set; }
}
