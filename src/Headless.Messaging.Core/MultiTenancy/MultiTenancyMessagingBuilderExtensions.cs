// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.Messaging.MultiTenancy;

/// <summary>
/// <see cref="MessagingBuilder"/> extensions for opt-in multi-tenancy propagation.
/// </summary>
public static class MultiTenancyMessagingBuilderExtensions
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

/// <summary>
/// Hosted service that validates a real <see cref="ICurrentTenant"/> implementation is registered
/// when <c>AddTenantPropagation()</c> was called. Throws <see cref="InvalidOperationException"/>
/// during <c>StartAsync</c> if the framework's fallback <see cref="NullCurrentTenant"/> is the
/// only registration — silent no-op propagation is hard to diagnose at runtime.
/// </summary>
internal sealed class TenantPropagationStartupValidator(ICurrentTenant currentTenant) : IHostedService
{
    private readonly ICurrentTenant _currentTenant = Argument.IsNotNull(currentTenant);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_currentTenant is NullCurrentTenant)
        {
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
}
