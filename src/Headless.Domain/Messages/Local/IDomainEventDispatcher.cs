// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>Dispatches captured domain events inline within the active unit of work to handlers for their exact runtime payload type.</summary>
/// <remarks>Dispatch preserves supplied identity and lineage. Capture new events at the emission boundary with <see cref="EventContext.Capture{TPayload}"/>.</remarks>
[PublicAPI]
public interface IDomainEventDispatcher
{
    /// <summary>Dispatches an existing event without allocating identity or restamping lineage.</summary>
    /// <typeparam name="TPayload">The event payload type.</typeparam>
    /// <param name="context">The event context to dispatch.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A value task representing the asynchronous dispatch operation.</returns>
    ValueTask DispatchAsync<TPayload>(EventContext<TPayload> context, CancellationToken cancellationToken = default)
        where TPayload : class;
}
