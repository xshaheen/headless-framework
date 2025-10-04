// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class MigrateDbContextExtensions
{
    public static void MigrateDbContext<TContext>(this IServiceProvider services)
        where TContext : DbContext
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        context.Database.Migrate();
    }

    public static void MigrateDbContextByFactory<TContext>(this IServiceProvider services)
        where TContext : DbContext
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        using var context = factory.CreateDbContext();
        context.Database.Migrate();
    }

    public static void EnsureDbCreated<TContext>(this IServiceProvider services)
        where TContext : DbContext
    {
        using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        context.Database.EnsureCreated();
    }

    public static void EnsureDbCreatedByFactory<TContext>(this IServiceProvider services)
        where TContext : DbContext
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        using var context = factory.CreateDbContext();
        context.Database.EnsureCreated();
    }

    public static async Task MigrateDbContextAsync<TContext>(
        this IServiceProvider services,
        CancellationToken token = default
    )
        where TContext : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        await context.Database.MigrateAsync(token);
    }

    public static async Task MigrateDbContextByFactoryAsync<TContext>(
        this IServiceProvider services,
        CancellationToken token = default
    )
        where TContext : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        await using var context = await factory.CreateDbContextAsync(token);
        await context.Database.MigrateAsync(token);
    }

    public static async Task EnsureDbCreatedAsync<TContext>(
        this IServiceProvider services,
        CancellationToken token = default
    )
        where TContext : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        await context.Database.EnsureCreatedAsync(token);
    }

    public static async Task EnsureDbCreatedByFactoryAsync<TContext>(
        this IServiceProvider services,
        CancellationToken token = default
    )
        where TContext : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        await using var context = await factory.CreateDbContextAsync(token);
        await context.Database.EnsureCreatedAsync(token);
    }
}
