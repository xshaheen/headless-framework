// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;

namespace Headless.EntityFramework;

[PublicAPI]
public static class MultiTenancySetup
{
    /// <summary>Configures Entity Framework tenant posture through the root Headless tenancy builder.</summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The Entity Framework tenancy configuration callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    public static HeadlessTenancyBuilder EntityFramework(
        this HeadlessTenancyBuilder builder,
        Action<HeadlessEntityFrameworkTenancyBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        configure(new HeadlessEntityFrameworkTenancyBuilder(builder));

        return builder;
    }
}

/// <summary>Records tenant posture for Headless Entity Framework services.</summary>
public sealed class HeadlessEntityFrameworkTenancyBuilder
{
    public const string Seam = "EntityFramework";
    public static readonly string[] ResolveFromClaimsCapability = ["guard-tenant-writes", "ef-owned-bypass"];

    private readonly HeadlessTenancyBuilder _builder;

    internal HeadlessEntityFrameworkTenancyBuilder(HeadlessTenancyBuilder builder)
    {
        _builder = Argument.IsNotNull(builder);
    }

    /// <summary>Enables the EF tenant write guard.</summary>
    /// <param name="configure">Optional write guard options.</param>
    /// <returns>The same Entity Framework tenancy builder.</returns>
    public HeadlessEntityFrameworkTenancyBuilder GuardTenantWrites(Action<TenantWriteGuardOptions>? configure = null)
    {
        _builder.Services.AddHeadlessTenantWriteGuard(configure);
        _builder.RecordSeam(Seam, TenantPostureStatuses.Guarded, ResolveFromClaimsCapability);

        return this;
    }
}

/// <summary>Options for the opt-in EF tenant write guard.</summary>
public sealed class TenantWriteGuardOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether tenant-owned writes require an ambient tenant
    /// unless a scoped bypass is active.
    /// </summary>
    public bool IsEnabled { get; set; }
}
