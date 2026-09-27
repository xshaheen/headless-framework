// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.EntityFramework;

[PublicAPI]
public static class SetupEntityFrameworkTenancy
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

/// <summary>
/// Emits a startup error for each guard the EF seam recorded (<c>guard-tenant-writes</c>,
/// <c>guard-tenant-reads</c>) whose <see cref="TenantGuardOptions"/> flag resolves to <see langword="false"/>,
/// typically because a later options registration clobbered the <c>PostConfigure</c> contribution. Surfaces the
/// mismatch at startup so operators are not surprised by silent loss of a guard.
/// </summary>
internal sealed class EntityFrameworkTenantGuardStartupValidator(IOptions<TenantGuardOptions> options)
    : IHeadlessTenancyValidator
{
    private const string _Seam = HeadlessEntityFrameworkTenancyBuilder.Seam;

    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var capabilities = context.Manifest.GetSeam(_Seam)?.Capabilities ?? [];

        if (
            capabilities.Contains(HeadlessEntityFrameworkTenancyBuilder.GuardTenantWritesLabel, StringComparer.Ordinal)
            && !options.Value.GuardWrites
        )
        {
            yield return HeadlessTenancyDiagnostic.Error(
                _Seam,
                "HEADLESS_TENANCY_EF_WRITE_GUARD_DISABLED",
                "Headless EntityFramework seam recorded guard-tenant-writes but TenantGuardOptions.GuardWrites "
                    + "resolved to false at startup. A later override of TenantGuardOptions clobbered the "
                    + "PostConfigure contribution applied by GuardTenantWrites(). Move the override before "
                    + "AddHeadlessTenancy(...) or remove it."
            );
        }

        if (
            capabilities.Contains(HeadlessEntityFrameworkTenancyBuilder.GuardTenantReadsLabel, StringComparer.Ordinal)
            && !options.Value.GuardReads
        )
        {
            yield return HeadlessTenancyDiagnostic.Error(
                _Seam,
                "HEADLESS_TENANCY_EF_READ_GUARD_DISABLED",
                "Headless EntityFramework seam recorded guard-tenant-reads but TenantGuardOptions.GuardReads "
                    + "resolved to false at startup. A later override of TenantGuardOptions clobbered the "
                    + "PostConfigure contribution applied by GuardTenantReads(). Move the override before "
                    + "AddHeadlessTenancy(...) or remove it."
            );
        }
    }
}
