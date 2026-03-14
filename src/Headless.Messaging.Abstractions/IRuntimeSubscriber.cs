// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Represents a runtime message handler that executes inside the same scoped consume pipeline as class-based handlers.
/// </summary>
/// <typeparam name="TMessage">The message type handled by the delegate.</typeparam>
/// <param name="context">The typed consume context for the current message.</param>
/// <param name="services">The scoped services for the current execution.</param>
/// <param name="cancellationToken">The cancellation token for the current execution.</param>
public delegate ValueTask RuntimeConsumeHandler<TMessage>(
    ConsumeContext<TMessage> context,
    IServiceProvider services,
    CancellationToken cancellationToken
)
    where TMessage : class;

/// <summary>
/// Controls how runtime subscription conflicts are handled.
/// </summary>
public enum RuntimeSubscriptionDuplicateBehavior
{
    /// <summary>
    /// Reject duplicate registrations with an exception.
    /// </summary>
    Reject = 0,

    /// <summary>
    /// Ignore duplicate registrations and keep the existing subscription attached.
    /// </summary>
    Ignore = 1,

    /// <summary>
    /// Replace the existing duplicate registration with the new subscription.
    /// </summary>
    Replace = 2,
}

/// <summary>
/// Options used when attaching a runtime message handler to the broker subscription pipeline.
/// </summary>
public sealed class RuntimeSubscriptionOptions
{
    /// <summary>
    /// Gets or sets the explicit topic to subscribe to.
    /// When omitted, the topic is resolved from configured mappings or deterministic conventions for the message type.
    /// </summary>
    public string? Topic { get; init; }

    /// <summary>
    /// Gets or sets the explicit consumer group.
    /// When omitted, the group is resolved from conventions using the deterministic handler identity.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// Gets or sets the concurrency limit for the runtime handler.
    /// Defaults to <c>1</c>.
    /// </summary>
    public byte Concurrency { get; init; } = 1;

    /// <summary>
    /// Gets or sets the explicit handler identity to use for diagnostics and default group generation.
    /// This is required for anonymous or compiler-generated delegates because the default policy fails fast when the identity is not deterministic.
    /// </summary>
    public string? HandlerId { get; init; }

    /// <summary>
    /// Gets or sets how duplicate runtime registrations are handled.
    /// Defaults to <see cref="RuntimeSubscriptionDuplicateBehavior.Reject" /> so duplicate runtime registrations fail fast unless an explicit opt-out is chosen.
    /// </summary>
    public RuntimeSubscriptionDuplicateBehavior DuplicateBehavior { get; init; } =
        RuntimeSubscriptionDuplicateBehavior.Reject;
}

/// <summary>
/// Represents an attached runtime subscription registration.
/// </summary>
public sealed class RuntimeSubscriptionHandle(Func<ValueTask> unsubscribe) : IAsyncDisposable
{
    private int _disposed;

    internal static RuntimeSubscriptionHandle Detached(
        string topic,
        string group,
        string handlerId,
        string? subscriptionId = null
    ) =>
        new(() => ValueTask.CompletedTask)
        {
            Topic = topic,
            Group = group,
            HandlerId = handlerId,
            SubscriptionId = subscriptionId,
            IsAttached = false,
        };

    internal static RuntimeSubscriptionHandle Attached(
        string subscriptionId,
        string topic,
        string group,
        string handlerId,
        Func<ValueTask> unsubscribe
    ) =>
        new(unsubscribe)
        {
            Topic = topic,
            Group = group,
            HandlerId = handlerId,
            SubscriptionId = subscriptionId,
            IsAttached = true,
        };

    /// <summary>
    /// Gets the runtime subscription id when the handler is attached.
    /// </summary>
    public string? SubscriptionId { get; private init; }

    /// <summary>
    /// Gets the resolved topic for the runtime handler.
    /// </summary>
    public string Topic { get; private init; } = string.Empty;

    /// <summary>
    /// Gets the resolved group for the runtime handler.
    /// </summary>
    public string Group { get; private init; } = string.Empty;

    /// <summary>
    /// Gets the deterministic handler identity for the runtime handler.
    /// </summary>
    public string HandlerId { get; private init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the subscription is currently attached.
    /// </summary>
    public bool IsAttached { get; private set; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            await unsubscribe().ConfigureAwait(false);
        }
        finally
        {
            IsAttached = false;
        }
    }
}

/// <summary>
/// Attaches and detaches ephemeral runtime message handlers.
/// Runtime delegates share scoped DI, filters, diagnostics, correlation, and failure semantics with class-based <see cref="IConsume{TMessage}" /> handlers.
/// </summary>
public interface IRuntimeSubscriber
{
    /// <summary>
    /// Attaches a runtime handler for the specified message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type handled by the runtime delegate.</typeparam>
    /// <param name="handler">The runtime delegate to execute for matching messages.</param>
    /// <param name="options">Optional overrides for topic, group, concurrency, handler identity, and duplicate behavior.</param>
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
    /// <returns><c>true</c> when a subscription was detached; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Detach is atomic for future deliveries and does not cancel handlers that are already in flight.
    /// </remarks>
    ValueTask<bool> UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken = default);
}
