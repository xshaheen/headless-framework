// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.MultiTenancy;

/// <summary>Shared service registration helpers for Headless tenancy packages.</summary>
public static class HeadlessTenancyServiceCollectionExtensions
{
    /// <summary>Adds the shared tenant posture manifest and startup diagnostics.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The singleton manifest instance registered in the service collection.</returns>
    public static TenantPostureManifest AddHeadlessTenancyCore(this IServiceCollection services)
    {
        Argument.IsNotNull(services);

        var manifest = services.GetOrAddTenantPostureManifest();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HeadlessTenancyStartupValidator>());

        return manifest;
    }

    /// <summary>Gets or adds the singleton tenant posture manifest.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The singleton manifest instance registered in the service collection.</returns>
    internal static TenantPostureManifest GetOrAddTenantPostureManifest(this IServiceCollection services)
    {
        Argument.IsNotNull(services);

        var existing = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(TenantPostureManifest));

        if (existing?.ImplementationInstance is TenantPostureManifest manifest)
        {
            return manifest;
        }

        manifest = new TenantPostureManifest();
        services.AddSingleton(manifest);

        return manifest;
    }
}
