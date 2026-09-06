// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Initialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.MultiTenancy;

/// <summary>Configures the opt-in tenant catalog through the root Headless tenancy builder.</summary>
[PublicAPI]
public static class SetupHeadlessTenancyCatalog
{
    /// <summary>
    /// Configures the tenant catalog: <see cref="TenantCatalogOptions"/>, exactly one storage provider
    /// (<c>UseInMemory</c> / <c>UseConfiguration</c> / <c>UseEntityFramework</c>), the catalog service,
    /// the <see cref="ICurrentTenantInfo"/> accessor, and posture recording.
    /// </summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The catalog configuration callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Zero or more than one storage provider was registered on the callback, or a storage provider was
    /// already registered for the catalog on this host (R18).
    /// </exception>
    public static HeadlessTenancyBuilder Catalog(
        this HeadlessTenancyBuilder builder,
        Action<HeadlessTenancyCatalogSetupBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var setup = new HeadlessTenancyCatalogSetupBuilder(builder.Services);
        configure(setup);

        _AddCatalogCore(builder, setup);

        return builder;
    }

    private static void _AddCatalogCore(HeadlessTenancyBuilder builder, HeadlessTenancyCatalogSetupBuilder setup)
    {
        var services = builder.Services;

        services.GuardSingleStorageProvider(
            setup.Extensions.Count,
            setup.Extensions.Count == 1 ? setup.Extensions.Single().GetType().FullName ?? "unknown" : "unknown",
            "Headless.MultiTenancy.Catalog",
            ["UseInMemory", "UseConfiguration", "UseEntityFramework"],
            static name => new TenantCatalogStorageProviderRegistration(name)
        );

        // Registered unconditionally so IOptions<TenantCatalogOptions> resolves with framework defaults
        // even when the app never calls HeadlessTenancyCatalogSetupBuilder.Configure(...).
        services.AddOptions<TenantCatalogOptions, TenantCatalogOptionsValidator>();

        // Singleton: the ignored-identifiers FrozenSet is derived once from options and reused across
        // every scoped TenantCatalogService instance, avoiding a per-request list scan.
        services.AddSingleton<TenantCatalogIgnoredIdentifierSet>();

        foreach (var extension in setup.Extensions)
        {
            extension.AddServices(services);
        }

        services.AddScoped<TenantCatalogService>();
        services.AddScoped<ITenantCatalogService>(sp => sp.GetRequiredService<TenantCatalogService>());

        // Overrides the NullCurrentTenantInfo default that AddHeadlessTenancyCore registered — Catalog(...)
        // always runs after that registration since it is only reachable through the AddHeadlessTenancy
        // configure callback.
        services.Replace(ServiceDescriptor.Scoped<ICurrentTenantInfo, TenantCatalogCurrentTenantInfo>());

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, TenantCatalogPostureValidator>()
        );

        builder.RecordSeam(
            TenantCatalogPosture.Seam,
            TenantPostureStatus.Configured,
            TenantCatalogPosture.AccessorCapability
        );
    }

    private sealed record TenantCatalogStorageProviderRegistration(string Provider);
}
