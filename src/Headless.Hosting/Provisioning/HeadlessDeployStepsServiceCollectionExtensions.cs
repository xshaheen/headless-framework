// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.Hosting;

/// <summary>
/// Runs the deploy steps, <c>resources provision</c> and <c>seed</c>, when the host starts, so a development or test
/// host needs no separate deploy step.
/// </summary>
[PublicAPI]
public static class HeadlessDeployStepsServiceCollectionExtensions
{
    /// <summary>
    /// Runs the deploy steps when the host starts, before any other hosted service starts: first provisions every
    /// resource (<see cref="DeployStepsOnStartupOptions.Resources" />), then runs every seeder
    /// (<see cref="DeployStepsOnStartupOptions.Seed" />). By default it does both. Calling it again configures the same
    /// steps.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Chooses the steps; omit it to provision and seed.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    /// <remarks>
    /// A failed step fails the host start and names every failed resource or the failed seeder. A purge or delete a
    /// provisioner does not support is skipped. Guard the call on the environment to keep it out of production, for
    /// example <c>if (builder.Environment.IsDevelopment())</c>, where the deployment runs <c>resources provision</c> and
    /// <c>seed</c> instead. <see cref="ResourceStartupAction.ProvisionAndPurge" /> fails the host start in an
    /// environment outside <see cref="ResourceProvisioningOptions.DestructiveEnvironments" />.
    /// </remarks>
    public static IServiceCollection AddDeployStepsOnStartup(
        this IServiceCollection services,
        Action<DeployStepsOnStartupOptions>? configure = null
    )
    {
        Argument.IsNotNull(services);

        services.AddOptions<DeployStepsOnStartupOptions>();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        if (_IsRegistered(services))
        {
            return services;
        }

        services.AddSingleton<ResourceProvisioningStartupService>();

        // First, because the host starts lifecycle services in registration order and every initializer or consumer
        // registered before this call would otherwise reach a resource that does not exist yet.
        services.Insert(
            0,
            ServiceDescriptor.Singleton<IHostedService>(static sp =>
                sp.GetRequiredService<ResourceProvisioningStartupService>()
            )
        );

        // At the call site, not first: seeders write through services the features registered before this call.
        services.AddSingleton<SeedStartupService>();
        services.AddSingleton<IHostedService>(static sp => sp.GetRequiredService<SeedStartupService>());

        return services;
    }

    /// <summary>
    /// Calls <see cref="AddDeployStepsOnStartup" /> only when nothing registered the startup deploy steps yet, so a
    /// test host's default never replaces the steps the application chose.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Chooses the steps to register when none are.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    public static IServiceCollection TryAddDeployStepsOnStartup(
        this IServiceCollection services,
        Action<DeployStepsOnStartupOptions>? configure = null
    )
    {
        Argument.IsNotNull(services);

        return _IsRegistered(services) ? services : services.AddDeployStepsOnStartup(configure);
    }

    private static bool _IsRegistered(IServiceCollection services) =>
        services.Any(static descriptor => descriptor.ServiceType == typeof(ResourceProvisioningStartupService));
}

/// <summary>Which deploy steps <c>AddDeployStepsOnStartup</c> runs when the host starts.</summary>
[PublicAPI]
public sealed class DeployStepsOnStartupOptions
{
    /// <summary>What to do to the provisioned resources. Default: <see cref="ResourceStartupAction.Provision" />.</summary>
    public ResourceStartupAction Resources { get; set; } = ResourceStartupAction.Provision;

    /// <summary>
    /// Whether to run every seeder after the resources are provisioned, as the <c>seed</c> verb does. Default:
    /// <see langword="true" />.
    /// </summary>
    public bool Seed { get; set; } = true;
}
