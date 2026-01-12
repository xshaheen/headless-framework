// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Framework.Hosting.Seeders;

[PublicAPI]
public static class DbSeedersExtensions
{
    public static IServiceCollection AddPreSeeder<T>(this IServiceCollection services)
        where T : class, IPreSeeder
    {
        Argument.IsNotNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Transient<IPreSeeder, T>());
        services.TryAddTransient<T>();

        return services;
    }

    public static IServiceCollection AddSeeder<T>(this IServiceCollection services)
        where T : class, ISeeder
    {
        Argument.IsNotNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeeder, T>());
        services.TryAddTransient<T>();

        return services;
    }

    public static async Task PreSeedAsync(this IServiceProvider services, bool runInParallel = false)
    {
        Argument.IsNotNull(services);

        await using var scope = services.CreateAsyncScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ISeeder>>();

        var preSeeders = scope
            .ServiceProvider.GetServices<IPreSeeder>()
            .Select(x => (Seeder: x, Type: x.GetType()))
            .OrderBy(x => x.Type.GetCustomAttribute<SeederPriorityAttribute>()?.Priority ?? 0);

        logger.LogInformation(">>> Pre-Seeding");

        var cancellationToken =
            scope.ServiceProvider.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None;

        if (runInParallel)
        {
            await Parallel.ForEachAsync(
                preSeeders,
                cancellationToken,
                async (x, ct) => await x.Seeder.SeedAsync(ct).AnyContext()
            );
        }
        else
        {
            foreach (var (seeder, type) in preSeeders)
            {
                logger.LogInformation(">>> Pre-Seeding using {TypeName}", type.GetFriendlyTypeName());
                await seeder.SeedAsync(cancellationToken);
            }
        }

        logger.LogInformation(">>> Pre-Seeding completed");
    }

    public static async Task SeedAsync(this IServiceProvider services, bool runInParallel = false)
    {
        Argument.IsNotNull(services);

        await using var scope = services.CreateAsyncScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ISeeder>>();

        var seeders = scope
            .ServiceProvider.GetServices<ISeeder>()
            .Select(x => (Seeder: x, Type: x.GetType()))
            .OrderBy(x => x.Type.GetCustomAttribute<SeederPriorityAttribute>()?.Priority ?? 0);

        logger.LogInformation(">>> Seeding");

        var cancellationToken =
            scope.ServiceProvider.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None;

        if (runInParallel)
        {
            await Parallel.ForEachAsync(
                seeders,
                cancellationToken,
                async (x, ct) => await x.Seeder.SeedAsync(ct).AnyContext()
            );
        }
        else
        {
            foreach (var (seeder, type) in seeders)
            {
                logger.LogInformation(">>> Seeding using {TypeName}", type.GetFriendlyTypeName());
                await seeder.SeedAsync(cancellationToken);
            }
        }

        logger.LogInformation(">>> Seeding completed");
    }
}
