// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.MultiTenancy;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

        services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();

        // Fail fast at startup when only the framework's fallback NullCurrentTenant is registered;
        // see TenantPropagationStartupValidator for the diagnostic contract.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TenantPropagationStartupValidator>());

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
            ServiceDescriptor.Singleton<IHostedService, MessagingTenantRequiredStartupValidator>()
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

        var currentTenant = context.Services.GetService<ICurrentTenant>();
        var realCurrentTenant = currentTenant is not null and not NullCurrentTenant;

        if (otherSeamsContributeTenant || realCurrentTenant)
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

/// <summary>
/// Hosted service that validates a real <see cref="ICurrentTenant"/> implementation is registered
/// when messaging tenant propagation was configured. Throws <see cref="InvalidOperationException"/>
/// during <c>StartAsync</c> if the framework's fallback <see cref="NullCurrentTenant"/> is the
/// only registration — silent no-op propagation is hard to diagnose at runtime.
/// </summary>
internal sealed partial class TenantPropagationStartupValidator(
    ICurrentTenant currentTenant,
    ILogger<TenantPropagationStartupValidator> logger
) : IHostedService
{
    private const string _DiagnosticCode = "HEADLESS_TENANCY_MESSAGING_PROPAGATION_NULL_CURRENT_TENANT";

    private readonly ICurrentTenant _currentTenant = Argument.IsNotNull(currentTenant);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_currentTenant is NullCurrentTenant)
        {
            LogPropagationNullCurrentTenant(logger, _DiagnosticCode, HeadlessMessagingTenancyBuilder.Seam);

            throw new InvalidOperationException(
                "Headless messaging tenant propagation was configured but the only ICurrentTenant "
                    + $"registration is {nameof(NullCurrentTenant)} — propagation would be a silent no-op. "
                    + "Register a real ICurrentTenant implementation (typically via "
                    + "AddHeadlessInfrastructure(), AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims())), "
                    + "AddHeadlessDbContextServices(), or by overriding the registration in DI) "
                    + "BEFORE calling AddHeadlessMessaging."
            );
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 1,
        EventName = "HeadlessTenancyPropagationNullCurrentTenant",
        Level = LogLevel.Error,
        Message = "Headless messaging tenant propagation validation failed ({Code}) on seam {Seam}: ICurrentTenant resolves to NullCurrentTenant."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogPropagationNullCurrentTenant(ILogger logger, string code, string seam);
}

/// <summary>
/// Hosted service that fails fast when the messaging seam recorded <c>require-tenant-on-publish</c>
/// posture but <see cref="MessagingOptions.TenantContextRequired"/> ends up <see langword="false"/>
/// at runtime (typically because a later <c>Configure&lt;MessagingOptions&gt;</c> call clobbered the
/// <c>PostConfigure</c> contribution). Surfaces the mismatch at startup so operators are not surprised
/// by silent loss of the strict-publish guard.
/// </summary>
internal sealed partial class MessagingTenantRequiredStartupValidator(
    IOptions<MessagingOptions> options,
    TenantPostureManifest manifest,
    ILogger<MessagingTenantRequiredStartupValidator> logger
) : IHostedService
{
    private const string _DiagnosticCode = "HEADLESS_TENANCY_MESSAGING_REQUIRE_TENANT_DISABLED";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var messagingSeam = manifest.GetSeam(HeadlessMessagingTenancyBuilder.Seam);

        var recordedRequireTenant =
            messagingSeam?.Capabilities.Contains(
                HeadlessMessagingTenancyBuilder.RequireTenantOnPublishCapability,
                StringComparer.Ordinal
            ) == true;

        if (recordedRequireTenant && !options.Value.TenantContextRequired)
        {
            LogRequireTenantDisabled(logger, _DiagnosticCode, HeadlessMessagingTenancyBuilder.Seam);

            throw new InvalidOperationException(
                "Headless messaging seam recorded require-tenant-on-publish but MessagingOptions.TenantContextRequired "
                    + "resolved to false at startup. A later Configure<MessagingOptions>(...) call clobbered the "
                    + "PostConfigure contribution applied by RequireTenantOnPublish(). Move the override before "
                    + "AddHeadlessTenancy(...) or remove it."
            );
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 2,
        EventName = "HeadlessTenancyRequireTenantDisabled",
        Level = LogLevel.Error,
        Message = "Headless messaging require-tenant-on-publish validation failed ({Code}) on seam {Seam}: MessagingOptions.TenantContextRequired resolved to false."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogRequireTenantDisabled(ILogger logger, string code, string seam);
}
