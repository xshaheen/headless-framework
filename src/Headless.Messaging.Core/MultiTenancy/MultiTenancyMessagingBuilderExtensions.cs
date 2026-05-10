// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;

namespace Headless.Messaging.MultiTenancy;

/// <summary>
/// <see cref="MessagingBuilder"/> extensions for opt-in multi-tenancy propagation.
/// </summary>
public static class MultiTenancyMessagingBuilderExtensions
{
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
    /// Requires a real <see cref="Headless.Abstractions.ICurrentTenant"/> implementation in DI.
    /// Without one, the framework's fallback <see cref="Headless.Abstractions.NullCurrentTenant"/>
    /// makes <see cref="TenantPropagationPublishFilter"/> a silent no-op (ambient
    /// <see cref="Headless.Abstractions.ICurrentTenant.Id"/> is always <see langword="null"/>).
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

        builder.AddSubscribeFilter<TenantPropagationConsumeFilter>();
        builder.AddPublishFilter<TenantPropagationPublishFilter>();

        return builder;
    }
}
