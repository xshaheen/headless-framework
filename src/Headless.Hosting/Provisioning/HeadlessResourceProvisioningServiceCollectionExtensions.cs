// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Hosting;

/// <summary>Registers resource provisioners and configures how they run.</summary>
[PublicAPI]
public static class HeadlessResourceProvisioningServiceCollectionExtensions
{
    /// <summary>Registers one resource provisioner, built from the service provider when an operation first needs it.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="provisionerFactory">Builds the provisioner.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services" /> or <paramref name="provisionerFactory" /> is null.
    /// </exception>
    public static IServiceCollection AddResourceProvisioner(
        this IServiceCollection services,
        Func<IServiceProvider, IResourceProvisioner> provisionerFactory
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(provisionerFactory);

        // Add, not TryAddEnumerable: every factory descriptor has the same implementation type, so TryAddEnumerable
        // would keep only the first provisioner.
        services.AddSingleton(provisionerFactory);

        return services._AddStartupChecks();
    }

    /// <summary>
    /// Registers a source of resource provisioners, once per source type, so a feature's setup may call it every time
    /// it runs.
    /// </summary>
    /// <typeparam name="TSource">The source; resolved from the container.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    public static IServiceCollection AddResourceProvisionerSource<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSource
    >(this IServiceCollection services)
        where TSource : class, IResourceProvisionerSource
    {
        Argument.IsNotNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IResourceProvisionerSource, TSource>());

        return services._AddStartupChecks();
    }

    /// <summary>Configures how resources are provisioned, such as where data-deleting operations may run.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Changes the options.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> or <paramref name="configure" /> is null.</exception>
    public static IServiceCollection ConfigureResourceProvisioning(
        this IServiceCollection services,
        Action<ResourceProvisioningOptions> configure
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configure);

        return services.Configure<ResourceProvisioningOptions, ResourceProvisioningOptionsValidator>(configure);
    }

    // Once per host: the first provisioner brings the two startup checks that read every provisioner's StartupCheck.
    private static IServiceCollection _AddStartupChecks(this IServiceCollection services)
    {
        if (
            services.Any(static d =>
                d.ServiceType == typeof(IHeadlessStartupValidator)
                && d.ImplementationType == typeof(ResourceStartupValidator)
            )
        )
        {
            return services;
        }

        services.AddOptions<ResourceProvisioningOptions>();
        services.AddStartupValidator<ResourceStartupValidator>();
        services.AddWarmup<ResourceReadinessWarmup>();

        return services;
    }
}
