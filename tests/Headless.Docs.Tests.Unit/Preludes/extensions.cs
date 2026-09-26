// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application models, repositories, and variables the extensions guide's examples assume.

global using static ExtensionsAmbient;
using Headless.Primitives;
using Microsoft.EntityFrameworkCore;

public sealed class User
{
    public required string Name { get; init; }
}

public sealed class Product
{
    public decimal Price { get; init; }

    public bool IsActive { get; init; }
}

public interface IUserRepository
{
    Task<User?> FindAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
}

#pragma warning disable IDE1006 // Ambient members mirror the locals and fields the examples use.
public static class ExtensionsAmbient
{
    public static IUserRepository _repo => null!;

    public static ApiResult<User> result => default;

    public static IEnumerable<User> users => [];

    public static Task ProcessAsync(User user) => Task.CompletedTask;

    public static Task ProcessAsync(User user, int index, CancellationToken cancellationToken) => Task.CompletedTask;

    public static Stream source => null!;

    public static Stream destination => null!;

    public static string key => "";

    public static AppDbContext dbContext => null!;

    public static string email => "";

    public static string tenantId => "";
}
