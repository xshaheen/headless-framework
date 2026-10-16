// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers stateful resources, and optionally sets them up when the host starts.</summary>
[PublicAPI]
public static class HeadlessStatefulResourceServiceCollectionExtensions
{
    /// <summary>Registers one stateful resource, built from the service provider when an operation first needs it.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="resourceFactory">Builds the resource.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> or <paramref name="resourceFactory" /> is null.</exception>
    public static IServiceCollection AddHeadlessStatefulResource(
        this IServiceCollection services,
        Func<IServiceProvider, IStatefulResource> resourceFactory
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(resourceFactory);

        // Add, not TryAddEnumerable: every factory descriptor has the same implementation type, so TryAddEnumerable
        // would keep only the first resource.
        services.AddSingleton(resourceFactory);

        return services;
    }

    /// <summary>
    /// Registers a source of stateful resources, once per source type, so a feature's setup may call it every time it
    /// runs.
    /// </summary>
    /// <typeparam name="TSource">The source; resolved from the container.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    public static IServiceCollection AddHeadlessStatefulResourceSource<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSource
    >(this IServiceCollection services)
        where TSource : class, IStatefulResourceSource
    {
        Argument.IsNotNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatefulResourceSource, TSource>());

        return services;
    }

    /// <summary>
    /// Sets up every stateful resource when the host starts, before any other hosted service runs, so a development or
    /// test host needs no separate <c>resources setup</c> step. Calling it again replaces the action.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="action">Whether to only set up the resources, or also clear their data.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    /// <remarks>
    /// A failed setup fails the host start and names every failed resource. An operation a resource does not support is
    /// skipped. Guard the call on the environment to keep it out of production, for example
    /// <c>if (builder.Environment.IsDevelopment())</c>, where the deployment runs <c>resources setup</c> instead.
    /// <see cref="StatefulResourceStartupAction.ResetState" /> fails the host start in the Production environment.
    /// </remarks>
    public static IServiceCollection AddHeadlessResourceSetupOnStartup(
        this IServiceCollection services,
        StatefulResourceStartupAction action = StatefulResourceStartupAction.SetupOnly
    )
    {
        Argument.IsNotNull(services);

        services.Configure<StatefulResourceStartupOptions>(options => options.Action = action);

        if (!_IsSetupOnStartupRegistered(services))
        {
            services.AddSingleton<StatefulResourceStartupService>();

            // First, because the host starts lifecycle services in registration order and every initializer or
            // consumer registered before this call would otherwise reach a resource that does not exist yet.
            services.Insert(
                0,
                ServiceDescriptor.Singleton<IHostedService>(static sp =>
                    sp.GetRequiredService<StatefulResourceStartupService>()
                )
            );
        }

        return services;
    }

    /// <summary>
    /// Calls <see cref="AddHeadlessResourceSetupOnStartup" /> only when nothing registered startup setup yet, so a test
    /// host's default never replaces the action the application chose.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="action">The action to register when none is.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    public static IServiceCollection TryAddHeadlessResourceSetupOnStartup(
        this IServiceCollection services,
        StatefulResourceStartupAction action = StatefulResourceStartupAction.SetupOnly
    )
    {
        Argument.IsNotNull(services);

        return _IsSetupOnStartupRegistered(services) ? services : services.AddHeadlessResourceSetupOnStartup(action);
    }

    private static bool _IsSetupOnStartupRegistered(IServiceCollection services) =>
        services.Any(static descriptor => descriptor.ServiceType == typeof(StatefulResourceStartupService));
}
