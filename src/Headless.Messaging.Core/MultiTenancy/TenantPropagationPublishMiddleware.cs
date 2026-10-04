// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.MultiTenancy;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging;

/// <summary>Stamps <see cref="MessageOptions.TenantId"/> from the ambient <see cref="ICurrentTenant.Id"/>.</summary>
[PublicAPI]
public sealed class TenantPropagationPublishMiddleware(
    ICurrentTenant currentTenant,
    ILogger<TenantPropagationPublishMiddleware>? logger = null
) : IPublishMiddleware<PublishContext>
{
    /// <summary>Framework priority for tenant stamping middleware.</summary>
    public const int Priority = -1000;

    private readonly ICurrentTenant _currentTenant = Argument.IsNotNull(currentTenant);

    /// <inheritdoc/>
    public async ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(next);

        if (
            context.Options?.SuppressAmbientBusinessContext != true
            && context.Options?.TenantId is null
            && _ResolveAmbientTenant(_currentTenant, logger) is { } ambientTenantId
        )
        {
            // Stamp the tenant on the record the caller passed: `with` keeps its runtime type (QueueOptions,
            // PublishOptions, or an enlisted write's OutboxOptions) and every other field, so the factory still sees
            // the caller's message name, headers, correlation, and callback. Only a publish without options gets the
            // lane's autonomous record.
            MessageOptions stamped =
                context.Options is { } options ? options with { TenantId = ambientTenantId }
                : context.Lane is MessageLane.Queue ? new QueueOptions { TenantId = ambientTenantId }
                : new PublishOptions { TenantId = ambientTenantId };
            context.WithOptions(stamped);
        }

        await next().ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the ambient tenant a message may carry, or <see langword="null"/> when there is none. A blank tenant is
    /// treated as absent, and one longer than <see cref="MessageOptions.TenantIdMaxLength"/> is dropped and logged rather
    /// than failing the send, so a bad ambient source never blocks publishing.
    /// </summary>
    private static string? _ResolveAmbientTenant(ICurrentTenant currentTenant, ILogger? logger)
    {
        if (currentTenant.Id is not { } ambientTenantId || string.IsNullOrWhiteSpace(ambientTenantId))
        {
            return null;
        }

        if (ambientTenantId.Length > MessageOptions.TenantIdMaxLength)
        {
            logger?.AmbientTenantPropagationDropped(ambientTenantId.Length);
            return null;
        }

        return ambientTenantId;
    }
}
