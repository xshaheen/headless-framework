// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Framework.Hosting.Seeders;

[PublicAPI]
public static class DbSeedersExtensions
{
    public static void AddPreSeeder<T>(this IServiceCollection services)
        where T : class, IPreSeeder
    {
        services.TryAddTransient<IPreSeeder, T>();
        services.TryAddTransient<T>();
    }

    public static void AddSeeder<T>(this IServiceCollection services)
        where T : class, ISeeder
    {
        services.TryAddTransient<ISeeder, T>();
        services.TryAddTransient<T>();
    }

    public static async Task PreSeedAsync(this IServiceProvider services, bool runInParallel = false)
    {
        await using var scope = services.CreateAsyncScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ISeeder>>();

        var preSeeders = scope
            .ServiceProvider.GetServices<IPreSeeder>()
            .Select(x => (Seeder: x, Type: x.GetType()))
            .OrderBy(x => x.Type.GetCustomAttribute<SeederPriorityAttribute>()?.Priority ?? 0);

        logger.LogInformation(">>> Pre-Seeding");

        if (runInParallel)
        {
            await Parallel.ForEachAsync(preSeeders, async (x, _) => await x.Seeder.SeedAsync());
        }
        else
        {
            foreach (var (seeder, type) in preSeeders)
            {
                logger.LogInformation(">>> Pre-Seeding using {TypeName}", type.GetFriendlyTypeName());
                await seeder.SeedAsync();
            }
        }

        logger.LogInformation(">>> Pre-Seeding completed");
    }

    public static async Task SeedAsync(this IServiceProvider services, bool runInParallel = false)
    {
        await using var scope = services.CreateAsyncScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ISeeder>>();

        var seeders = scope
            .ServiceProvider.GetServices<ISeeder>()
            .Select(x => (Seeder: x, Type: x.GetType()))
            .OrderBy(x => x.Type.GetCustomAttribute<SeederPriorityAttribute>()?.Priority ?? 0);

        logger.LogInformation(">>> Seeding");

        if (runInParallel)
        {
            await Parallel.ForEachAsync(seeders, async (x, _) => await x.Seeder.SeedAsync());
        }
        else
        {
            foreach (var (seeder, type) in seeders)
            {
                logger.LogInformation(">>> Seeding using {TypeName}", type.GetFriendlyTypeName());
                await seeder.SeedAsync();
            }
        }

        logger.LogInformation(">>> Seeding completed");
    }
}
