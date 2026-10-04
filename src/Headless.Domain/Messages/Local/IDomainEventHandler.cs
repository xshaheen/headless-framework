// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>
/// Defines a handler for a specific domain event type.
/// </summary>
/// <remarks>
/// Apply <see cref="DomainEventHandlerOrderAttribute"/> to control invocation order when multiple handlers
/// are registered for the same event type.
/// </remarks>
/// <typeparam name="TPayload">The concrete domain event payload type this handler processes.</typeparam>
[PublicAPI]
public interface IDomainEventHandler<TPayload>
    where TPayload : class
{
    /// <summary>Handles the domain event.</summary>
    /// <param name="context">The event context containing occurrence identity and business lineage.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A value task representing the asynchronous handling operation.</returns>
    ValueTask HandleAsync(EventContext<TPayload> context, CancellationToken cancellationToken = default);
}
