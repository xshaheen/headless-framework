// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Attaches and detaches ephemeral runtime message handlers.
/// Runtime delegates share scoped DI, middleware, diagnostics, correlation, and failure semantics with class-based <see cref="IConsume{TMessage}" /> handlers.
/// </summary>
[PublicAPI]
public interface IRuntimeSubscriber
{
    /// <summary>
    /// Attaches a runtime handler for the specified message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type handled by the runtime delegate.</typeparam>
    /// <param name="handler">The runtime delegate to execute for matching messages.</param>
    /// <param name="options">Optional overrides for message name, identity, concurrency, handler identity, and duplicate behavior.</param>
    /// <param name="cancellationToken">The cancellation token for the registration operation.</param>
    /// <returns>A handle that can be disposed to detach the runtime subscription.</returns>
    /// <remarks>
    /// Future deliveries stop as soon as the registration is detached.
    /// In-flight handler executions continue to completion with their existing scoped services.
    /// </remarks>
    ValueTask<RuntimeSubscriptionHandle> SubscribeAsync<TMessage>(
        RuntimeConsumeHandler<TMessage> handler,
        RuntimeSubscriptionOptions? options = null,
        CancellationToken cancellationToken = default
    )
        where TMessage : class;

    /// <summary>
    /// Detaches a previously attached runtime subscription.
    /// </summary>
    /// <param name="subscriptionId">The runtime subscription id returned by <see cref="SubscribeAsync{TMessage}" />.</param>
    /// <param name="cancellationToken">The cancellation token for the detach operation.</param>
    /// <returns><see langword="true"/> when a subscription was detached; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Detach is atomic for future deliveries and does not cancel handlers that are already in flight.
    /// </remarks>
    ValueTask<bool> UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken = default);
}
