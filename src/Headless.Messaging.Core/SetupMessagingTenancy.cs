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

    internal static MessagingBuilder AddTenantPropagationServices(this MessagingBuilder builder)
    {
        Argument.IsNotNull(builder);

        builder
            .AddBusConsumeMiddleware<TenantPropagationConsumeMiddleware>()
            .WithPriority(TenantPropagationConsumeMiddleware.Priority)
            .AddBusPublishMiddleware<TenantPropagationPublishMiddleware>()
            .WithPriority(TenantPropagationPublishMiddleware.Priority);

        // Standardized ICurrentTenant primitives — see Headless.Messaging.Core/Setup.cs for the
        // rationale. CurrentTenant.Id returns null when no AsyncLocal value is set so the publish
        // strict-tenancy guard still fails fast under TenantContextRequired = true.
        builder.Services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        builder.Services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();

        // Routes through the unified IHeadlessTenancyValidator collection aggregated by
        // HeadlessTenancyStartupValidator (an IHeadlessStartupValidator) — runs before any
        // IHostedService.StartAsync so a misconfigured tenancy posture fails fast.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, TenantPropagationStartupValidator>()
        );

        return builder;
    }
}
