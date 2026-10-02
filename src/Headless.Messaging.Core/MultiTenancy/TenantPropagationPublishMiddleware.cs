// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.MultiTenancy;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.MultiTenancy;

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
            && ResolveAmbientTenant(_currentTenant, logger) is { } ambientTenantId
        )
        {
            // Stamp TenantId on a concrete options record that matches the publish intent so
            // downstream middleware and the factory receive the correct derived type.
            MessageOptions stamped = context.Lane switch
            {
                MessageLane.Queue => (context.Options as QueueOptions ?? new QueueOptions()) with
                {
                    TenantId = ambientTenantId,
                },
                _ => (context.Options as PublishOptions ?? new PublishOptions()) with { TenantId = ambientTenantId },
            };
            context.WithOptions(stamped);
        }

        await next().ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the ambient tenant a message may carry, or <see langword="null"/> when there is none. A blank tenant is
    /// treated as absent, and one longer than <see cref="MessageOptions.TenantIdMaxLength"/> is dropped and logged rather
    /// than failing the send, so a bad ambient source never blocks publishing. Every path that stamps the ambient tenant
    /// applies these same rules.
    /// </summary>
    internal static string? ResolveAmbientTenant(ICurrentTenant currentTenant, ILogger? logger)
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
