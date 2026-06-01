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
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSeeder<T>()
            where T : class, ISeeder
        {
            Argument.IsNotNull(services);

            services.TryAddEnumerable(ServiceDescriptor.Transient<ISeeder, T>());
            services.TryAddTransient<T>();

            return services;
        }
    }

    /// <summary>
    /// Runs all registered <see cref="ISeeder"/>s ascending by <see cref="SeederPriorityAttribute"/>
    /// (default priority <c>0</c>; migrations seed first via <see cref="int.MinValue"/>). In parallel
    /// mode each seeder resolves in its own DI scope (DbContext is not thread-safe).
    /// </summary>
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

        logger.LogSeeding();

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
                var typeName = type.GetFriendlyTypeName();
                logger.LogSeedingUsing(typeName);
                var seeder = (ISeeder)scope.ServiceProvider.GetRequiredService(type);
                await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        logger.LogSeedingCompleted();
    }
}
