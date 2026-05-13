// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.MultiTenancy;

/// <summary>Provides the root setup surface for Headless tenant posture configuration.</summary>
[PublicAPI]
public static class SetupHeadlessTenancy
{
    /// <summary>
    /// Adds the shared tenancy manifest and lets installed Headless packages contribute seam-specific tenant posture.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">The tenancy configuration callback.</param>
    /// <returns>The same host application builder.</returns>
    public static IHostApplicationBuilder AddHeadlessTenancy(
        this IHostApplicationBuilder builder,
        Action<HeadlessTenancyBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var manifest = builder.Services.AddHeadlessTenancyCore();
        configure(new HeadlessTenancyBuilder(builder, manifest));

        return builder;
    }

    /// <summary>Adds the shared tenant posture manifest and startup diagnostics.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The singleton manifest instance registered in the service collection.</returns>
    internal static TenantPostureManifest AddHeadlessTenancyCore(this IServiceCollection services)
    {
        Argument.IsNotNull(services);

        var manifest = services.GetOrAddTenantPostureManifest();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HeadlessTenancyStartupValidator>());

        return manifest;
    }

    /// <summary>Gets or adds the singleton tenant posture manifest.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The singleton manifest instance registered in the service collection.</returns>
    /// <remarks>
    /// Returns an existing singleton instance when one is already registered. When a non-instance
    /// registration is found (factory or open-type), replaces it with a fresh singleton instance —
    /// this closes the factory blind-spot in the previous design where a consumer-supplied factory
    /// or open-type registration was hidden behind the <c>ImplementationInstance</c> check, leading
    /// to a second instance being added and splitting posture across two manifests.
    /// </remarks>
    internal static TenantPostureManifest GetOrAddTenantPostureManifest(this IServiceCollection services)
    {
        Argument.IsNotNull(services);

        var existing = services.LastOrDefault(d => d.ServiceType == typeof(TenantPostureManifest));

        if (existing?.ImplementationInstance is TenantPostureManifest manifest)
        {
            return manifest;
        }

        if (existing is not null)
        {
            services.RemoveAll<TenantPostureManifest>();
        }

        manifest = new TenantPostureManifest();
        services.AddSingleton(manifest);

        return manifest;
    }
}
