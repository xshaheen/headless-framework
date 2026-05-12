// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;

namespace Headless.EntityFramework;

[PublicAPI]
public static class SetupMultiTenancy
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
    /// <summary>The seam name reported in the tenant posture manifest.</summary>
    public const string Seam = "EntityFramework";

    /// <summary>Capability labels reported by <see cref="GuardTenantWrites"/>.</summary>
    public static readonly string[] GuardTenantWritesCapabilities = ["guard-tenant-writes", "ef-owned-bypass"];

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
        _builder.RecordSeam(Seam, TenantPostureStatus.Guarded, GuardTenantWritesCapabilities);

        return this;
    }
}
