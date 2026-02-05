// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Hosting.Seeders;

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

        // Collect types first to avoid shared DI scope in parallel mode (DbContext is not thread-safe)
        var seederTypes = scope
            .ServiceProvider.GetServices<IPreSeeder>()
            .Select(x => x.GetType())
            .OrderBy(x => x.GetCustomAttribute<SeederPriorityAttribute>()?.Priority ?? 0)
            .ToList();

        logger.LogInformation(">>> Pre-Seeding");

        var cancellationToken =
            scope.ServiceProvider.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None;

        if (runInParallel)
        {
            await Parallel.ForEachAsync(
                seederTypes,
                cancellationToken,
                async (type, ct) =>
                {
                    await using var innerScope = services.CreateAsyncScope();
                    var seeder = (IPreSeeder)innerScope.ServiceProvider.GetRequiredService(type);
                    await seeder.SeedAsync(ct).ConfigureAwait(false);
                }
            );
        }
        else
        {
            foreach (var type in seederTypes)
            {
                logger.LogInformation(">>> Pre-Seeding using {TypeName}", type.GetFriendlyTypeName());
                var seeder = (IPreSeeder)scope.ServiceProvider.GetRequiredService(type);
                await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        logger.LogInformation(">>> Pre-Seeding completed");
    }

    public static async Task SeedAsync(this IServiceProvider services, bool runInParallel = false)
    {
        Argument.IsNotNull(services);

        await using var scope = services.CreateAsyncScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ISeeder>>();

        // Collect types first to avoid shared DI scope in parallel mode (DbContext is not thread-safe)
        var seederTypes = scope
            .ServiceProvider.GetServices<ISeeder>()
            .Select(x => x.GetType())
            .OrderBy(x => x.GetCustomAttribute<SeederPriorityAttribute>()?.Priority ?? 0)
            .ToList();

        logger.LogInformation(">>> Seeding");

        var cancellationToken =
            scope.ServiceProvider.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None;

        if (runInParallel)
        {
            await Parallel.ForEachAsync(
                seederTypes,
                cancellationToken,
                async (type, ct) =>
                {
                    await using var innerScope = services.CreateAsyncScope();
                    var seeder = (ISeeder)innerScope.ServiceProvider.GetRequiredService(type);
                    await seeder.SeedAsync(ct).ConfigureAwait(false);
                }
            );
        }
        else
        {
            foreach (var type in seederTypes)
            {
                logger.LogInformation(">>> Seeding using {TypeName}", type.GetFriendlyTypeName());
                var seeder = (ISeeder)scope.ServiceProvider.GetRequiredService(type);
                await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        logger.LogInformation(">>> Seeding completed");
    }
}
