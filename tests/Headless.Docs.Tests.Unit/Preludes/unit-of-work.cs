// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application commands, messages, contexts, and variables the unit-of-work guide's examples assume.

global using static UnitOfWorkAmbient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

public readonly record struct OrderId(Guid Value);

public sealed class Order
{
    public OrderId Id { get; set; }
}

public sealed class Reservation(OrderId orderId)
{
    public Guid Id { get; set; }

    public OrderId OrderId { get; } = orderId;
}

public sealed record PlaceOrder(OrderId OrderId);

public sealed record OrderPlaced(OrderId OrderId);

public sealed record ExpireReservation(OrderId OrderId);

public sealed record ReleaseReservation(OrderId OrderId);

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Reservation> Reservations => Set<Reservation>();
}

public sealed class MyDbContext(DbContextOptions<MyDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Reservation> Reservations => Set<Reservation>();
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class UnitOfWorkAmbient
{
    public static IUnitOfWorkFactory factory => null!;

    public static MyDbContext db => null!;

    public static Order order => null!;

    public static OrderId orderId => default;

    public static DateTimeOffset dueAt => default;

    public static string connectionString => "";

    public static NpgsqlConnection connection => null!;

    public static ICache cache => null!;

    public static string key => "";
}
