// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;

namespace Headless.Messaging.MultiTenancy;

/// <summary>
/// Stamps <see cref="PublishOptions.TenantId"/> from the ambient <see cref="ICurrentTenant.Id"/> at
/// publish time so messages carry their originating tenant on the wire.
/// </summary>
/// <remarks>
/// <para>
/// The filter only stamps when:
/// <list type="bullet">
/// <item><description>An ambient tenant is set (<see cref="ICurrentTenant.Id"/> is non-null), and</description></item>
/// <item><description>The caller has not already set <see cref="PublishOptions.TenantId"/> explicitly.</description></item>
/// </list>
/// A caller-set value is preserved verbatim — system messages override propagation by setting
/// <see cref="PublishOptions.TenantId"/> explicitly or by publishing while ambient tenant is null.
/// </para>
/// <para>
/// The filter writes only the typed <see cref="PublishOptions.TenantId"/> property, never the raw
/// <see cref="Headers.TenantId"/> header. The publish pipeline's 4-case integrity policy
/// (<see cref="PublishOptions.TenantId"/> docs) maps the typed property to the wire header at
/// stamping time.
/// </para>
/// <para>
/// Applications that do not register a real <see cref="ICurrentTenant"/> implementation observe a
/// <see cref="NullCurrentTenant"/> with <see cref="ICurrentTenant.Id"/> always <see langword="null"/>,
/// in which case this filter is a silent no-op. Register a real <see cref="ICurrentTenant"/> (typically
/// via <c>Headless.Api</c>'s multi-tenancy setup) before <c>AddHeadlessMessaging</c> to enable
/// propagation.
/// </para>
/// </remarks>
public sealed class TenantPropagationPublishFilter(ICurrentTenant currentTenant) : PublishFilter
{
    private readonly ICurrentTenant _currentTenant =
        Argument.IsNotNull(currentTenant);

    /// <inheritdoc/>
    public override ValueTask OnPublishExecutingAsync(PublishingContext context)
    {
        Argument.IsNotNull(context);

        if (context.Options?.TenantId is null && _currentTenant.Id is { } ambientTenantId)
        {
            context.Options = (context.Options ?? new PublishOptions()) with { TenantId = ambientTenantId };
        }

        return ValueTask.CompletedTask;
    }
}
