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
        /// <summary>Registers <typeparamref name="T"/> as an <see cref="ISeeder"/>.</summary>
        /// <typeparam name="T">The seeder type to register.</typeparam>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the service collection is <see langword="null"/>.</exception>
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
    /// mode each seeder resolves in its own DI scope (DbContext is not thread-safe) and execution
    /// ordering across seeders is not guaranteed.
    /// </summary>
    /// <param name="services">The root service provider.</param>
    /// <param name="runInParallel">When <see langword="true"/>, seeders run concurrently, each in its own scope.</param>
    /// <param name="cancellationToken">
    /// Cancels seeding. Linked with <see cref="IHostApplicationLifetime.ApplicationStopping"/> when an
    /// <see cref="IHostApplicationLifetime"/> is registered, so host shutdown also cancels seeding.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when seeding is canceled via <paramref name="cancellationToken"/> or host shutdown.</exception>
    public static async Task SeedAsync(
        this IServiceProvider services,
        bool runInParallel = false,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(services);

        await using var scope = services.CreateAsyncScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ISeeder>>();

        var applicationStopping =
            scope.ServiceProvider.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, applicationStopping);
        var token = linkedCts.Token;

        // Resolve once and order by priority. Sequential mode reuses these instances directly; parallel
        // mode keeps only their types and re-resolves one per scope (DbContext is not thread-safe) — so
        // parallel mode constructs each seeder twice, which is unavoidable from an IServiceProvider alone.
        var seeders = scope
            .ServiceProvider.GetServices<ISeeder>()
            .OrderBy(x => x.GetType().GetCustomAttribute<SeederPriorityAttribute>()?.Priority ?? 0)
            .ToList();

        logger.LogSeeding();

        if (runInParallel)
        {
            await Parallel.ForEachAsync(
                seeders.Select(x => x.GetType()),
                token,
                async (type, ct) =>
                {
                    var typeName = type.GetFriendlyTypeName();
                    logger.LogSeedingUsing(typeName);
                    await using var innerScope = services.CreateAsyncScope();
                    var seeder = (ISeeder)innerScope.ServiceProvider.GetRequiredService(type);
                    await seeder.SeedAsync(ct).ConfigureAwait(false);
                }
            );
        }
        else
        {
            foreach (var seeder in seeders)
            {
                var typeName = seeder.GetType().GetFriendlyTypeName();
                logger.LogSeedingUsing(typeName);
                await seeder.SeedAsync(token).ConfigureAwait(false);
            }
        }

        logger.LogSeedingCompleted();
    }
}
