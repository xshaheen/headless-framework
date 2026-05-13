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
    /// <summary>The seam name reported in the tenant posture manifest.</summary>
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
        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, EntityFrameworkTenantWriteGuardStartupValidator>()
        );
        _builder.RecordSeam(Seam, TenantPostureStatus.Guarded, _GuardTenantWritesCapabilityLabels);

        return this;
    }
}

/// <summary>
/// Emits a startup error when the EF seam recorded the <c>guard-tenant-writes</c> capability but
/// <see cref="TenantWriteGuardOptions.IsEnabled"/> resolves to <see langword="false"/> (typically
/// because a later <c>Configure&lt;TenantWriteGuardOptions&gt;</c> call clobbered the
/// <c>PostConfigure</c> contribution). Surfaces the mismatch at startup so operators are not
/// surprised by silent loss of the guard.
/// </summary>
internal sealed class EntityFrameworkTenantWriteGuardStartupValidator(IOptions<TenantWriteGuardOptions> options)
    : IHeadlessTenancyValidator
{
    private const string _Seam = HeadlessEntityFrameworkTenancyBuilder.Seam;

    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var efSeam = context.Manifest.GetSeam(_Seam);
        var recordedGuard =
            efSeam?.Capabilities.Contains(
                HeadlessEntityFrameworkTenancyBuilder.GuardTenantWritesLabel,
                StringComparer.Ordinal
            ) == true;

        if (!recordedGuard || options.Value.IsEnabled)
        {
            yield break;
        }

        yield return HeadlessTenancyDiagnostic.Error(
            _Seam,
            "HEADLESS_TENANCY_EF_WRITE_GUARD_DISABLED",
            "Headless EntityFramework seam recorded guard-tenant-writes but TenantWriteGuardOptions.IsEnabled "
                + "resolved to false at startup. A later Configure<TenantWriteGuardOptions>(...) call clobbered "
                + "the PostConfigure contribution applied by GuardTenantWrites(). Move the override before "
                + "AddHeadlessTenancy(...) or remove it."
        );
    }
}
