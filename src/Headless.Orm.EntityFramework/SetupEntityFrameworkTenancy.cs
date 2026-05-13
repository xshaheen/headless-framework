// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

    private static readonly string[] _GuardTenantWritesCapabilityLabels = ["guard-tenant-writes", "ef-owned-bypass"];

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
            ServiceDescriptor.Singleton<IHostedService, EntityFrameworkTenantWriteGuardStartupValidator>()
        );
        _builder.RecordSeam(Seam, TenantPostureStatus.Guarded, _GuardTenantWritesCapabilityLabels);

        return this;
    }
}

/// <summary>
/// Hosted service that fails fast when the EF seam recorded the <c>guard-tenant-writes</c> capability
/// but <see cref="TenantWriteGuardOptions.IsEnabled"/> resolved to <see langword="false"/> at startup
/// (typically because a later <c>Configure&lt;TenantWriteGuardOptions&gt;</c> call clobbered the
/// <c>PostConfigure</c> contribution). Surfaces the mismatch at startup so operators are not surprised
/// by silent loss of the guard.
/// </summary>
internal sealed partial class EntityFrameworkTenantWriteGuardStartupValidator(
    IOptions<TenantWriteGuardOptions> options,
    TenantPostureManifest manifest,
    ILogger<EntityFrameworkTenantWriteGuardStartupValidator> logger
) : IHostedService
{
    private const string DiagnosticCode = "HEADLESS_TENANCY_EF_WRITE_GUARD_DISABLED";
    private const string Seam = HeadlessEntityFrameworkTenancyBuilder.Seam;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var efSeam = manifest.GetSeam(Seam);
        var recordedGuard = efSeam?.Capabilities.Contains("guard-tenant-writes", StringComparer.Ordinal) == true;

        if (recordedGuard && !options.Value.IsEnabled)
        {
            LogGuardDisabled(logger, DiagnosticCode, Seam);
            throw new InvalidOperationException(
                "Headless EntityFramework seam recorded guard-tenant-writes but TenantWriteGuardOptions.IsEnabled "
                    + "resolved to false at startup. A later Configure<TenantWriteGuardOptions>(...) call clobbered "
                    + "the PostConfigure contribution applied by GuardTenantWrites(). Move the override before "
                    + "AddHeadlessTenancy(...) or remove it."
            );
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 1,
        EventName = "HeadlessTenancyEntityFrameworkWriteGuardDisabled",
        Level = LogLevel.Error,
        Message = "Headless EntityFramework write-guard validation failed ({Code}) on seam {Seam}: TenantWriteGuardOptions.IsEnabled resolved to false."
    )]
    private static partial void LogGuardDisabled(ILogger logger, string code, string seam);
}
