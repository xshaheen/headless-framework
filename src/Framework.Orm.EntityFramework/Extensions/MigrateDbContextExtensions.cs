// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class MigrateDbContextExtensions
{
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
