// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Initialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.MultiTenancy;

/// <summary>Configures where tenant data physically lives, through the root Headless tenancy builder.</summary>
[PublicAPI]
public static class SetupHeadlessTenancyDataPlacement
{
    /// <summary>The seam name recorded in the tenant posture manifest when a placement source is configured.</summary>
    public const string Seam = "DataPlacement";

    /// <summary>
    /// Configures the tenant data placement source: exactly one of <c>UseConfiguration</c> or
    /// <c>UseResolver&lt;T&gt;()</c>. Placements take effect only for data contexts registered as tenant-routed
    /// (for example <c>EntityFramework(ef =&gt; ef.RouteTenantData&lt;AppDbContext&gt;())</c>).
    /// </summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The placement configuration callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Zero or more than one placement source was configured, or a placement source was already configured on this host.
    /// </exception>
    public static HeadlessTenancyBuilder DataPlacement(
        this HeadlessTenancyBuilder builder,
        Action<HeadlessTenancyDataPlacementSetupBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var setup = new HeadlessTenancyDataPlacementSetupBuilder(builder.Services);
        configure(setup);

        builder.Services.GuardSingleStorageProvider(
            setup.Sources.Count,
            setup.Sources.Count == 1 ? setup.Sources[0].Label : "unknown",
            "Headless.MultiTenancy.DataPlacement",
            ["UseConfiguration", "UseResolver"],
            static name => new TenantDataPlacementSourceRegistration(name)
        );

        // Registered unconditionally so IOptions<TenantDataPlacementOptions> resolves with its defaults even when
        // the app never calls Configure(...).
        builder.Services.AddOptions<TenantDataPlacementOptions, TenantDataPlacementOptionsValidator>();

        // Resolved optionally by the tenancy entry points; it stays inert until a context is tenant-routed.
        builder.Services.TryAddSingleton<TenantDataPlacementPreloader>();

        var (label, register) = setup.Sources[0];
        register(builder.Services);

        builder.RecordSeam(Seam, TenantPostureStatus.Configured, label);

        return builder;
    }
}

/// <summary>Sentinel recording that a tenant data placement source was configured on this host.</summary>
/// <param name="Source">The configured source label.</param>
internal sealed record TenantDataPlacementSourceRegistration(string Source);
