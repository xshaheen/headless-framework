// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Dispatches captured domain events inline within the active unit of work to handlers for their exact runtime payload type.</summary>
/// <remarks>Dispatch preserves the supplied identity and lineage. Capture new events at the emission boundary with <see cref="EventContext.Capture{TPayload}"/>.</remarks>
[PublicAPI]
public interface IDomainEventDispatcher
{
    /// <summary>Dispatches an existing event without allocating identity or restamping lineage.</summary>
    ValueTask DispatchAsync<TPayload>(EventContext<TPayload> context, CancellationToken cancellationToken = default)
        where TPayload : class;
}
