// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Hosting;

[PublicAPI]
public static class DbSeedersExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <typeparamref name="T"/> as an <see cref="ISeeder"/>. Registering the same type again does nothing,
        /// so a feature's setup may run more than once.
        /// </summary>
        /// <typeparam name="T">The seeder type to register.</typeparam>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the service collection is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is already registered with a factory.</exception>
        public IServiceCollection AddSeeder<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T
        >()
            where T : class, ISeeder
        {
            Argument.IsNotNull(services);

            if (_Register(services, typeof(T), factory: null))
            {
                services.AddTransient<ISeeder, T>();
                services.TryAddTransient<T>();
            }

            return services;
        }

        /// <summary>
        /// Registers the seeder <paramref name="factory"/> builds. Registering the same type with the same factory again
        /// does nothing, so a feature's setup may run more than once; another factory for the same type is refused,
        /// because only one of the two could ever run.
        /// </summary>
        /// <typeparam name="T">The seeder type to register.</typeparam>
        /// <param name="factory">Builds the seeder from the scope it runs in.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">The service collection or <paramref name="factory"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is already registered another way.</exception>
        public IServiceCollection AddSeeder<T>(Func<IServiceProvider, T> factory)
            where T : class, ISeeder
        {
            Argument.IsNotNull(services);
            Argument.IsNotNull(factory);

            if (_Register(services, typeof(T), factory))
            {
                services.AddTransient<ISeeder>(factory);
                // By type too: the parallel SeedAsync re-resolves each seeder by its type in a scope of its own.
                services.TryAddTransient(factory);
            }

            return services;
        }
    }

    // Returns false when this exact registration already exists; throws when the type is registered differently.
    private static bool _Register(IServiceCollection services, Type seederType, Delegate? factory)
    {
        var existing = services
            .Select(static d => d.ImplementationInstance)
            .OfType<SeederRegistration>()
            .FirstOrDefault(r => r.SeederType == seederType);

        if (existing is not null)
        {
            if (ReferenceEquals(existing.Factory, factory))
            {
                return false;
            }

            throw new InvalidOperationException(
                $"Seeder {seederType.Name} is already registered {(existing.Factory is null ? "by type" : "with another factory")}; "
                    + "only one registration of a seeder type can run."
            );
        }

        services.AddSingleton(new SeederRegistration(seederType, factory));
        services.AddOptions<SeedingOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SeedingConfigurationCheck>());

        return true;
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
        var seeders = SeedRunner.Ordered(scope.ServiceProvider);

        logger.LogSeeding();

        if (runInParallel)
        {
            await Parallel
                .ForEachAsync(
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
                )
                .ConfigureAwait(false);
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
