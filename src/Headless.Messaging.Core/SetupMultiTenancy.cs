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

namespace Headless.Messaging;

public static class SetupMultiTenancy
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

    /// <summary>
    /// Registers <see cref="TenantPropagationPublishFilter"/> on the publish side and
    /// <see cref="TenantPropagationConsumeFilter"/> on the consume side so messages carry their
    /// originating tenant on the wire and consumers run under that tenant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Idempotent — calling more than once does not double-register either filter, since both
    /// underlying registrations use <c>TryAddEnumerable</c>.
    /// </para>
    /// <para>
    /// Requires a real <see cref="ICurrentTenant"/> implementation in DI. The framework fails fast
    /// at startup when only the fallback <see cref="NullCurrentTenant"/> is registered:
    /// a hosted-service validation runs once on application start and throws
    /// <see cref="InvalidOperationException"/> with a diagnostic message naming the missing service
    /// and pointing to the framework APIs that register a real tenant resolver. Register a real
    /// <c>ICurrentTenant</c> (typically via <c>AddHeadlessInfrastructure()</c>,
    /// <c>AddHeadlessMultiTenancy()</c>, <c>AddHeadlessDbContextServices()</c>, or by overriding the
    /// registration in DI) BEFORE calling <c>AddHeadlessMessaging</c>.
    /// </para>
    /// <para>
    /// Trust boundary: the consume filter trusts the inbound envelope. Topics exposed to external
    /// producers must layer envelope validation in front of this filter — see
    /// <see cref="TenantPropagationConsumeFilter"/> docs.
    /// </para>
    /// </remarks>
    /// <param name="builder">The messaging builder.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static MessagingBuilder AddTenantPropagation(this MessagingBuilder builder)
    {
        Argument.IsNotNull(builder);

        builder.Services.AddTenantPropagationServices();

        return builder;
    }

    internal static IServiceCollection AddTenantPropagationServices(this IServiceCollection services)
    {
        Argument.IsNotNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConsumeFilter, TenantPropagationConsumeFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPublishFilter, TenantPropagationPublishFilter>());

        // Fail fast at startup when only the framework's fallback NullCurrentTenant is registered;
        // see the AddTenantPropagation remarks for the diagnostic contract.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TenantPropagationStartupValidator>());

        return services;
    }
}

/// <summary>Records tenant posture for Headless messaging.</summary>
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
        if (!_builder.Services.Any(d => d.ServiceType == typeof(RequireTenantOnPublishSentinel)))
        {
            _builder.Services.AddSingleton<RequireTenantOnPublishSentinel>();
            _builder.Services.PostConfigure<MessagingOptions>(options => options.TenantContextRequired = true);
        }

        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, MessagingTenantRequiredCrossSeamValidator>()
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
/// when <c>AddTenantPropagation()</c> was called. Throws <see cref="InvalidOperationException"/>
/// during <c>StartAsync</c> if the framework's fallback <see cref="NullCurrentTenant"/> is the
/// only registration — silent no-op propagation is hard to diagnose at runtime.
/// </summary>
internal sealed partial class TenantPropagationStartupValidator(
    ICurrentTenant currentTenant,
    ILogger<TenantPropagationStartupValidator> logger
) : IHostedService
{
    private const string DiagnosticCode = "HEADLESS_TENANCY_MESSAGING_PROPAGATION_NULL_CURRENT_TENANT";
    private const string Seam = HeadlessMessagingTenancyBuilder.Seam;

    private readonly ICurrentTenant _currentTenant = Argument.IsNotNull(currentTenant);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_currentTenant is NullCurrentTenant)
        {
            LogPropagationNullCurrentTenant(logger, DiagnosticCode, Seam);
            throw new InvalidOperationException(
                $"AddTenantPropagation() was called but the only ICurrentTenant registration is "
                    + $"{nameof(NullCurrentTenant)} — propagation would be a silent no-op. "
                    + "Register a real ICurrentTenant implementation (typically via "
                    + "AddHeadlessInfrastructure(), AddHeadlessMultiTenancy(), "
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
    private static partial void LogPropagationNullCurrentTenant(ILogger logger, string code, string seam);
}
