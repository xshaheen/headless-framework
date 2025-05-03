// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MoreLinq;

namespace Framework.Hosting.Seeders;

[PublicAPI]
public static class DbSeedersExtensions
{
    public static void AddPreSeeder<T>(this IServiceCollection services)
        where T : class, IPreSeeder
    {
        services.AddTransient<IPreSeeder, T>().AddTransient<T>();
    }

    public static void AddSeeder<T>(this IServiceCollection services)
        where T : class, ISeeder
    {
        services.AddTransient<ISeeder, T>().AddTransient<T>();
    }

    public static async Task PreSeedAsync(this IServiceProvider services, bool runInParallel = true)
    {
        await using var scope = services.CreateAsyncScope();

        var preSeeders = scope.ServiceProvider
            .GetServices<IPreSeeder>()
            .OrderBy(x => x.GetType().GetCustomAttribute<SeederPriorityAttribute>()?.Priority ?? 0);

        if (runInParallel)
        {
            await Parallel.ForEachAsync(preSeeders, async (seeder, _) => await seeder.SeedAsync());
        }
        else
        {
            foreach (var seeder in preSeeders)
            {
                await seeder.SeedAsync();
            }
        }
    }

    public static async Task SeedAsync(this IServiceProvider services, bool runInParallel = true)
    {
        await using var scope = services.CreateAsyncScope();

        var preSeeders = scope.ServiceProvider
            .GetServices<ISeeder>()
            .OrderBy(x => x.GetType().GetCustomAttribute<SeederPriorityAttribute>()?.Priority ?? 0);

        if (runInParallel)
        {
            await Parallel.ForEachAsync(preSeeders, async (seeder, _) => await seeder.SeedAsync());
        }
        else
        {
            foreach (var seeder in preSeeders)
            {
                await seeder.SeedAsync();
            }
        }
    }
}
