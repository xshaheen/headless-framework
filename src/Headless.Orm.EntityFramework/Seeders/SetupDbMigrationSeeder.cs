// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Hosting.Seeders;

/// <summary>
/// Extension methods for registering <see cref="DbMigrationSeeder{TDbContext}"/> in the application's
/// seeder pipeline.
/// </summary>
[PublicAPI]
public static class SetupDbMigrationSeeder
{
    /// <summary>
    /// Registers a <see cref="DbMigrationSeeder{TDbContext}"/> that applies pending EF Core migrations
    /// for <typeparamref name="TContext"/> at startup.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="DbContext"/> to migrate.</typeparam>
    /// <param name="services">The service collection.</param>
    public static void AddDbMigrationSeeder<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddSeeder<DbMigrationSeeder<TContext>>();
    }
}
