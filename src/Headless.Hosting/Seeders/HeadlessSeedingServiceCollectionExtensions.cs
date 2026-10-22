// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Hosting;

/// <summary>Registers <see cref="ISeeder"/>s, which the deployment's <c>seed</c> verb runs.</summary>
[PublicAPI]
public static class HeadlessSeedingServiceCollectionExtensions
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

        return true;
    }
}
