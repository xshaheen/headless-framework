// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.EntityFramework.Contexts.Runtime;
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

    internal const string RouteTenantDataLabel = "route-tenant-data";

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
    /// Routes <typeparamref name="TContext"/> per tenant: each instance created under an ambient tenant connects to
    /// that tenant's schema or database from the configured <c>DataPlacement(...)</c> source and stays pinned to that
    /// tenant for its lifetime. Create routed contexts with
    /// <c>IDbContextFactory&lt;TContext&gt;.CreateDbContextAsync()</c>; injecting one directly under a tenant fails,
    /// because the placement must be resolved before the context builds its model. Without an ambient tenant the
    /// context uses its own registration (connection and <c>DefaultSchema</c>). A tenant with no placement is
    /// refused rather than sent to the shared database. Requires <c>DataPlacement(...)</c> on the same tenancy
    /// builder; startup fails otherwise.
    /// </summary>
    /// <typeparam name="TContext">The context type to route. It must not back an outbox, a Jobs store, or a
    /// host-level store (tenant catalog, Settings, Features, Permissions); those registrations fail startup over a
    /// routed context.</typeparam>
    /// <param name="configure">Optional per-context routing options.</param>
    /// <returns>The same Entity Framework tenancy builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="TenantDataRoutingOptions.MaxCachedSchemas"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="TContext"/> is already routed on this host.</exception>
    public HeadlessEntityFrameworkTenancyBuilder RouteTenantData<TContext>(
        Action<TenantDataRoutingOptions>? configure = null
    )
        where TContext : HeadlessDbContext
    {
        var services = _builder.Services;
        var options = new TenantDataRoutingOptions();
        configure?.Invoke(options);
        Argument.IsPositive(options.MaxCachedSchemas);

        if (
            services.Any(d =>
                d.ImplementationInstance is TenantDataRoutedContextRegistration registration
                && registration.ContextType == typeof(TContext)
            )
        )
        {
            throw new InvalidOperationException($"'{typeof(TContext).Name}' is already tenant-routed on this host.");
        }

        services.AddSingleton(new TenantDataRoutedContextRegistration(typeof(TContext)));
        services.AddSingleton(new HeadlessTenantRoutedContext(typeof(TContext), options.MaxCachedSchemas));

        services.TryAddSingleton<HeadlessTenantDataRouting>();
        services.TryAddScoped<HeadlessTenantPlacementPin>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, EntityFrameworkTenantRoutingStartupValidator>()
        );
        _builder.RecordSeam(Seam, TenantPostureStatus.Configured, RouteTenantDataLabel);

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

/// <summary>
/// Emits a startup error when a context is tenant-routed but no <c>DataPlacement(...)</c> source is configured:
/// every tenant would then have no placement and every routed context creation would fail.
/// </summary>
internal sealed class EntityFrameworkTenantRoutingStartupValidator : IHeadlessTenancyValidator
{
    private const string _Seam = HeadlessEntityFrameworkTenancyBuilder.Seam;

    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var routed =
            context
                .Manifest.GetSeam(_Seam)
                ?.Capabilities.Contains(
                    HeadlessEntityFrameworkTenancyBuilder.RouteTenantDataLabel,
                    StringComparer.Ordinal
                ) == true;

        if (!routed || context.Manifest.IsConfigured(SetupHeadlessTenancyDataPlacement.Seam))
        {
            yield break;
        }

        yield return HeadlessTenancyDiagnostic.Error(
            _Seam,
            "HEADLESS_TENANCY_EF_ROUTING_WITHOUT_PLACEMENT",
            "Headless EntityFramework seam routes tenant data (RouteTenantData) but no tenant data placement source "
                + "is configured, so every tenant-routed context would be refused. Call "
                + "DataPlacement(p => p.UseConfiguration(...)) or DataPlacement(p => p.UseResolver<T>()) on the same "
                + "tenancy builder."
        );
    }
}
