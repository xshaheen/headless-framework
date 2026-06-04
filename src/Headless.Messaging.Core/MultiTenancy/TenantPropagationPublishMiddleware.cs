// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Internal;
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

        if (context.Options?.TenantId is null && _currentTenant.Id is { } ambientTenantId)
        {
            if (string.IsNullOrWhiteSpace(ambientTenantId))
            {
                await next().ConfigureAwait(false);
                return;
            }

            if (ambientTenantId.Length > MessageOptions.TenantIdMaxLength)
            {
                logger?.AmbientTenantPropagationDropped(ambientTenantId.Length);
                await next().ConfigureAwait(false);
                return;
            }

            // Stamp TenantId on a concrete options record that matches the publish intent so
            // downstream middleware and the factory receive the correct derived type.
            MessageOptions stamped = context.IntentType switch
            {
                IntentType.Queue => (context.Options as EnqueueOptions ?? new EnqueueOptions()) with
                {
                    TenantId = ambientTenantId,
                },
                _ => (context.Options as PublishOptions ?? new PublishOptions()) with { TenantId = ambientTenantId },
            };
            context.WithOptions(stamped);
        }

        await next().ConfigureAwait(false);
    }
}
