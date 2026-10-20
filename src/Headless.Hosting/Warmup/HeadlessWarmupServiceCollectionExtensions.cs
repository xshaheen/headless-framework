// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Hosting;

/// <summary>Registers <see cref="IWarmup" />s, which the host runs in the background once it has started.</summary>
[PublicAPI]
public static class HeadlessWarmupServiceCollectionExtensions
{
    /// <summary>The name of the readiness health check that reports whether the host is warm.</summary>
    public const string HealthCheckName = "warmup";

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <typeparamref name="T" /> as an <see cref="IWarmup" />. Registering the same type again does
        /// nothing, so a feature's setup may run more than once. The first warm-up also registers the background runner
        /// and the <c>warmup</c> readiness health check.
        /// </summary>
        /// <typeparam name="T">The warm-up type, resolved from its own scope.</typeparam>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">The service collection is <see langword="null" />.</exception>
        public IServiceCollection AddWarmup<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T
        >()
            where T : class, IWarmup
        {
            Argument.IsNotNull(services);

            services.TryAddEnumerable(ServiceDescriptor.Transient<IWarmup, T>());

            return services._AddWarmupRunner();
        }

        /// <summary>
        /// Registers the warm-up <paramref name="factory" /> builds. Registering the same type again does nothing.
        /// </summary>
        /// <typeparam name="T">The warm-up type.</typeparam>
        /// <param name="factory">Builds the warm-up from the scope it runs in.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">
        /// The service collection or <paramref name="factory" /> is <see langword="null" />.
        /// </exception>
        public IServiceCollection AddWarmup<T>(Func<IServiceProvider, T> factory)
            where T : class, IWarmup
        {
            Argument.IsNotNull(services);
            Argument.IsNotNull(factory);

            services.TryAddEnumerable(ServiceDescriptor.Transient<IWarmup, T>(factory));

            return services._AddWarmupRunner();
        }

        private IServiceCollection _AddWarmupRunner()
        {
            if (services.Any(static d => d.ServiceType == typeof(WarmupRunner)))
            {
                return services;
            }

            services.AddInitializerHostedService<WarmupRunner>();

            // Reads the running host's state, so the pre-start 'check' verb skips it: a host that never started is
            // never warm.
            services.AddHeadlessHealthCheck(
                HealthCheckName,
                static (provider, _) =>
                    provider.GetRequiredService<WarmupRunner>().DescribePending() is { } pending
                        ? Task.FromException(new InvalidOperationException(pending))
                        : Task.CompletedTask,
                HeadlessHealthCheckTags.HostState
            );

            return services;
        }
    }
}
