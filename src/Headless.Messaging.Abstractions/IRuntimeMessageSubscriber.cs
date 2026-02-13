// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Delegate for runtime function-based message handlers.
/// </summary>
/// <typeparam name="TMessage">The handled message type.</typeparam>
/// <param name="serviceProvider">The scoped service provider for this invocation.</param>
/// <param name="context">The consume context for the incoming message.</param>
/// <param name="cancellationToken">The cancellation token.</param>
/// <returns>A task representing handler completion.</returns>
public delegate ValueTask RuntimeMessageHandler<TMessage>(
    IServiceProvider serviceProvider,
    ConsumeContext<TMessage> context,
    CancellationToken cancellationToken
)
    where TMessage : class;

/// <summary>
/// Identifies a runtime function subscription.
/// </summary>
/// <param name="MessageType">The handled message type.</param>
/// <param name="Topic">The subscribed topic.</param>
/// <param name="Group">The resolved consumer group.</param>
/// <param name="HandlerId">A deterministic handler identifier.</param>
public readonly record struct RuntimeSubscriptionKey(Type MessageType, string Topic, string Group, string HandlerId);

/// <summary>
/// Registers and removes runtime function subscriptions that are attached to the message broker.
/// </summary>
public interface IRuntimeMessageSubscriber
{
    /// <summary>
    /// Subscribes a runtime function handler.
    /// </summary>
    /// <typeparam name="TMessage">The handled message type.</typeparam>
    /// <param name="handler">The runtime handler.</param>
    /// <param name="topic">Optional topic. Uses conventions when omitted.</param>
    /// <param name="group">Optional group. Uses conventions/defaults when omitted.</param>
    /// <param name="handlerId">Optional handler identifier. A deterministic value is generated when omitted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created runtime subscription key.</returns>
    ValueTask<RuntimeSubscriptionKey> SubscribeAsync<TMessage>(
        RuntimeMessageHandler<TMessage> handler,
        string? topic = null,
        string? group = null,
        string? handlerId = null,
        CancellationToken cancellationToken = default
    )
        where TMessage : class;

    /// <summary>
    /// Subscribes a runtime function handler without explicit service-provider usage.
    /// </summary>
    /// <typeparam name="TMessage">The handled message type.</typeparam>
    /// <param name="handler">The runtime handler.</param>
    /// <param name="topic">Optional topic. Uses conventions when omitted.</param>
    /// <param name="group">Optional group. Uses conventions/defaults when omitted.</param>
    /// <param name="handlerId">Optional handler identifier. A deterministic value is generated when omitted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created runtime subscription key.</returns>
    ValueTask<RuntimeSubscriptionKey> SubscribeAsync<TMessage>(
        Func<ConsumeContext<TMessage>, CancellationToken, ValueTask> handler,
        string? topic = null,
        string? group = null,
        string? handlerId = null,
        CancellationToken cancellationToken = default
    )
        where TMessage : class;

    /// <summary>
    /// Unsubscribes a runtime function handler by key.
    /// </summary>
    /// <param name="subscriptionKey">The subscription key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> when a subscription was removed.</returns>
    ValueTask<bool> UnsubscribeAsync(
        RuntimeSubscriptionKey subscriptionKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Unsubscribes a runtime function handler by route information.
    /// </summary>
    /// <typeparam name="TMessage">The handled message type.</typeparam>
    /// <param name="topic">Optional topic. Uses conventions when omitted.</param>
    /// <param name="group">Optional group. Uses conventions/defaults when omitted.</param>
    /// <param name="handlerId">Optional handler identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> when a subscription was removed.</returns>
    ValueTask<bool> UnsubscribeAsync<TMessage>(
        string? topic = null,
        string? group = null,
        string? handlerId = null,
        CancellationToken cancellationToken = default
    )
        where TMessage : class;

    /// <summary>
    /// Lists all active runtime function subscriptions.
    /// </summary>
    /// <returns>The active runtime subscription keys.</returns>
    IReadOnlyList<RuntimeSubscriptionKey> ListSubscriptions();
}
