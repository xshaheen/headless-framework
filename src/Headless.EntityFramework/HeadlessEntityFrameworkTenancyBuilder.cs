// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.EntityFramework;

/// <summary>Records tenant posture for Headless Entity Framework services.</summary>
[PublicAPI]
public sealed class HeadlessEntityFrameworkTenancyBuilder
{
    /// <summary>
    /// The seam identifier used when recording this integration in the tenant posture manifest.
    /// </summary>
    public const string Seam = "EntityFramework";

    internal const string GuardTenantWritesLabel = "guard-tenant-writes";

    private static readonly string[] _GuardTenantWritesCapabilityLabels = [GuardTenantWritesLabel, "ef-owned-bypass"];

    /// <summary>
    /// Capability labels reported by <see cref="GuardTenantWrites"/>.
    /// Downstream consumers can introspect the capability labels recorded by the EF seam for posture
    /// assertions.
    /// </summary>
    public static IReadOnlyList<string> GuardTenantWritesCapabilities { get; } =
        Array.AsReadOnly(_GuardTenantWritesCapabilityLabels);

    internal const string GuardTenantReadsLabel = "guard-tenant-reads";

    private static readonly string[] _GuardTenantReadsCapabilityLabels = [GuardTenantReadsLabel];

    /// <summary>
    /// Capability labels reported by <see cref="GuardTenantReads"/>.
    /// Downstream consumers can introspect the capability labels recorded by the EF seam for posture
    /// assertions.
    /// </summary>
    public static IReadOnlyList<string> GuardTenantReadsCapabilities { get; } =
        Array.AsReadOnly(_GuardTenantReadsCapabilityLabels);

    private readonly HeadlessTenancyBuilder _builder;

    internal HeadlessEntityFrameworkTenancyBuilder(HeadlessTenancyBuilder builder)
    {
        _builder = Argument.IsNotNull(builder);
    }

    /// <summary>Enables the EF tenant write guard.</summary>
    /// <returns>The same Entity Framework tenancy builder.</returns>
    public HeadlessEntityFrameworkTenancyBuilder GuardTenantWrites()
    {
        _builder.Services.AddHeadlessTenantWriteGuard();
        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, EntityFrameworkTenantGuardStartupValidator>()
        );
        _builder.RecordSeam(Seam, TenantPostureStatus.Guarded, _GuardTenantWritesCapabilityLabels);

        return this;
    }

    /// <summary>
    /// Enables the EF tenant read guard: a query that applies the multi-tenancy filter to an entity whose
    /// tenant column is required throws <see cref="MissingTenantContextException"/> when it executes without
    /// an ambient tenant, instead of returning no rows. Entities with a nullable tenant column keep returning
    /// host rows. <c>IgnoreMultiTenancyFilter()</c> lifts the guard for one query;
    /// <see cref="ITenantWriteGuardBypass"/> does not.
    /// </summary>
    /// <returns>The same Entity Framework tenancy builder.</returns>
    public HeadlessEntityFrameworkTenancyBuilder GuardTenantReads()
    {
        _builder.Services.AddHeadlessTenantReadGuard();
        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, EntityFrameworkTenantGuardStartupValidator>()
        );
        _builder.RecordSeam(Seam, TenantPostureStatus.Guarded, _GuardTenantReadsCapabilityLabels);

        return this;
    }
}
