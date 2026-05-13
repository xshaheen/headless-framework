// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.MultiTenancy;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Messaging;

[PublicAPI]
public static class SetupMessagingTenancy
{
    /// <summary>Configures messaging tenant posture through the root Headless tenancy builder.</summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The messaging tenancy configuration callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    public static HeadlessTenancyBuilder Messaging(
        this HeadlessTenancyBuilder builder,
        Action<HeadlessMessagingTenancyBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        configure(new HeadlessMessagingTenancyBuilder(builder));

        return builder;
    }

    internal static IServiceCollection AddTenantPropagationServices(this IServiceCollection services)
    {
        Argument.IsNotNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConsumeFilter, TenantPropagationConsumeFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPublishFilter, TenantPropagationPublishFilter>());

        // Standardized ICurrentTenant primitives — see Headless.Messaging.Core/Setup.cs for the
        // rationale. CurrentTenant.Id returns null when no AsyncLocal value is set so the publish
        // strict-tenancy guard still fails fast under TenantContextRequired = true.
        services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();

        // Routes through the unified IHeadlessTenancyValidator collection aggregated by
        // HeadlessTenancyStartupValidator (IHostedLifecycleService) — runs in StartingAsync before any
        // IHostedService.StartAsync so a misconfigured tenancy posture fails fast.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, TenantPropagationStartupValidator>()
        );

        return services;
    }
}

/// <summary>Records tenant posture for Headless messaging.</summary>
[PublicAPI]
public sealed class HeadlessMessagingTenancyBuilder
{
    /// <summary>The seam name reported in the tenant posture manifest.</summary>
    public const string Seam = "Messaging";

    /// <summary>Capability label reported by <see cref="PropagateTenant"/>.</summary>
    public const string PropagateTenantCapability = "propagate-tenant";

    /// <summary>Capability label reported by <see cref="RequireTenantOnPublish"/>.</summary>
    public const string RequireTenantOnPublishCapability = "require-tenant-on-publish";

    private readonly HeadlessTenancyBuilder _builder;

    internal HeadlessMessagingTenancyBuilder(HeadlessTenancyBuilder builder)
    {
        _builder = Argument.IsNotNull(builder);
    }

    /// <summary>Registers publish and consume filters that propagate tenant context through messages.</summary>
    /// <returns>The same messaging tenancy builder.</returns>
    public HeadlessMessagingTenancyBuilder PropagateTenant()
    {
        _builder.Services.AddTenantPropagationServices();
        _builder.RecordSeam(Seam, TenantPostureStatus.Propagating, PropagateTenantCapability);

        return this;
    }

    /// <summary>Requires publish calls to resolve a tenant from publish options or ambient tenant context.</summary>
    /// <returns>The same messaging tenancy builder.</returns>
    public HeadlessMessagingTenancyBuilder RequireTenantOnPublish()
    {
        // Sentinel — guard the PostConfigure registration so repeated RequireTenantOnPublish()
        // calls do not register the same callback twice. The sentinel marker registration uses
        // a singleton presence check to ensure exactly-once PostConfigure wiring.
        if (_builder.Services.All(d => d.ServiceType != typeof(RequireTenantOnPublishSentinel)))
        {
            _builder.Services.AddSingleton<RequireTenantOnPublishSentinel>();
            _builder.Services.PostConfigure<MessagingOptions>(options => options.TenantContextRequired = true);
        }

        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, MessagingTenantRequiredCrossSeamValidator>()
        );
        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, MessagingTenantRequiredStartupValidator>()
        );
        _builder.RecordSeam(Seam, TenantPostureStatus.Enforcing, RequireTenantOnPublishCapability);

        return this;
    }
}

/// <summary>Sentinel for one-shot RequireTenantOnPublish PostConfigure registration.</summary>
internal sealed class RequireTenantOnPublishSentinel;

/// <summary>
/// Emits a startup warning when <c>RequireTenantOnPublish()</c> is configured in isolation: no other
/// seam (HTTP claim resolution, tenant propagation, or a real <see cref="ICurrentTenant"/>) contributes
/// the ambient tenant that publishes will require. The diagnostic is a warning rather than an error so
/// hosts that deliberately resolve the tenant via custom <c>ICurrentTenant</c> registrations are not
/// blocked at startup; the cross-seam validator only fires when no other tenancy seam recorded posture.
/// </summary>
internal sealed class MessagingTenantRequiredCrossSeamValidator : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var messagingSeam = context.Manifest.GetSeam(HeadlessMessagingTenancyBuilder.Seam);

        if (
            messagingSeam?.Capabilities.Contains(
                HeadlessMessagingTenancyBuilder.RequireTenantOnPublishCapability,
                StringComparer.Ordinal
            ) != true
        )
        {
            yield break;
        }

        var otherSeamsContributeTenant = context.Manifest.Seams.Any(seam =>
            !string.Equals(seam.Seam, HeadlessMessagingTenancyBuilder.Seam, StringComparison.Ordinal)
        );

        if (otherSeamsContributeTenant || _TenantSourceMissing.HasConsumerOverride(context.Services))
        {
            yield break;
        }

        yield return HeadlessTenancyDiagnostic.Warning(
            HeadlessMessagingTenancyBuilder.Seam,
            "HEADLESS_TENANCY_MESSAGING_REQUIRE_TENANT_ISOLATED",
            "RequireTenantOnPublish() is configured but no other Headless tenancy seam (HTTP claim "
                + "resolution, tenant propagation, EntityFramework, or a real ICurrentTenant registration) "
                + "contributes the ambient tenant required at publish time. Add a tenant source or register "
                + "a real ICurrentTenant before AddHeadlessMessaging."
        );
    }
}

/// <summary>Shared "no tenant source" predicate used by the messaging tenancy validators.</summary>
internal static class _TenantSourceMissing
{
    /// <summary>
    /// Returns <see langword="true"/> when the host registered a custom <see cref="ICurrentTenant"/> implementation
    /// beyond the framework defaults (<c>CurrentTenant</c> or <c>NullCurrentTenant</c>). Used by tenancy validators
    /// to recognize consumer-supplied tenant sources that bypass the seam manifest.
    /// </summary>
    public static bool HasConsumerOverride(IServiceProvider services)
    {
        var currentTenant = services.GetService<ICurrentTenant>();
        return currentTenant is not null and not CurrentTenant and not NullCurrentTenant;
    }
}

/// <summary>
/// Emits a startup error when messaging tenant propagation was configured but no other Headless
/// tenancy seam (HTTP claim resolution, EntityFramework, Mediator) is recorded in the posture
/// manifest AND no consumer-supplied <see cref="ICurrentTenant"/> override is registered.
/// Propagation under those conditions would be a silent no-op, and silent no-op propagation is hard
/// to diagnose at runtime.
/// </summary>
internal sealed class TenantPropagationStartupValidator : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var messagingSeam = context.Manifest.GetSeam(HeadlessMessagingTenancyBuilder.Seam);

        if (
            messagingSeam?.Capabilities.Contains(
                HeadlessMessagingTenancyBuilder.PropagateTenantCapability,
                StringComparer.Ordinal
            ) != true
        )
        {
            yield break;
        }

        var otherSeamsContributeTenant = context.Manifest.Seams.Any(seam =>
            !string.Equals(seam.Seam, HeadlessMessagingTenancyBuilder.Seam, StringComparison.Ordinal)
        );

        if (otherSeamsContributeTenant || _TenantSourceMissing.HasConsumerOverride(context.Services))
        {
            yield break;
        }

        yield return HeadlessTenancyDiagnostic.Error(
            HeadlessMessagingTenancyBuilder.Seam,
            "HEADLESS_TENANCY_MESSAGING_PROPAGATION_NULL_CURRENT_TENANT",
            "Headless messaging tenant propagation was configured but no other Headless tenancy seam "
                + "(HTTP claim resolution, EntityFramework, Mediator) is recorded and no consumer-supplied "
                + "ICurrentTenant override was registered — propagation would be a silent no-op. Register a "
                + "real tenant source via AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims())), "
                + "AddHeadlessDbContextServices(), or a custom ICurrentTenant implementation BEFORE calling "
                + "AddHeadlessMessaging."
        );
    }
}

/// <summary>
/// Emits a startup error when the messaging seam recorded <c>require-tenant-on-publish</c> posture
/// but <see cref="MessagingOptions.TenantContextRequired"/> resolves to <see langword="false"/>
/// (typically because a later <c>Configure&lt;MessagingOptions&gt;</c> call clobbered the
/// <c>PostConfigure</c> contribution). Surfaces the mismatch at startup so operators are not
/// surprised by silent loss of the strict-publish guard.
/// </summary>
internal sealed class MessagingTenantRequiredStartupValidator(IOptions<MessagingOptions> options)
    : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var messagingSeam = context.Manifest.GetSeam(HeadlessMessagingTenancyBuilder.Seam);

        var recordedRequireTenant =
            messagingSeam?.Capabilities.Contains(
                HeadlessMessagingTenancyBuilder.RequireTenantOnPublishCapability,
                StringComparer.Ordinal
            ) == true;

        if (!recordedRequireTenant || options.Value.TenantContextRequired)
        {
            yield break;
        }

        yield return HeadlessTenancyDiagnostic.Error(
            HeadlessMessagingTenancyBuilder.Seam,
            "HEADLESS_TENANCY_MESSAGING_REQUIRE_TENANT_DISABLED",
            "Headless messaging seam recorded require-tenant-on-publish but MessagingOptions.TenantContextRequired "
                + "resolved to false at startup. A later Configure<MessagingOptions>(...) call clobbered the "
                + "PostConfigure contribution applied by RequireTenantOnPublish(). Move the override before "
                + "AddHeadlessTenancy(...) or remove it."
        );
    }
}
