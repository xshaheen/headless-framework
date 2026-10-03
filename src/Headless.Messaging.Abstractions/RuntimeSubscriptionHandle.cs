// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Represents an attached runtime subscription registration.
/// </summary>
[PublicAPI]
public sealed class RuntimeSubscriptionHandle(Func<ValueTask> unsubscribe) : IAsyncDisposable
{
    private int _disposed;

    internal static RuntimeSubscriptionHandle Detached(
        string messageName,
        string identity,
        string handlerId,
        string? subscriptionId = null
    )
    {
        return new(() => ValueTask.CompletedTask)
        {
            MessageName = messageName,
            Identity = identity,
            HandlerId = handlerId,
            SubscriptionId = subscriptionId,
            IsAttached = false,
        };
    }

    internal static RuntimeSubscriptionHandle Attached(
        string subscriptionId,
        string messageName,
        string identity,
        string handlerId,
        Func<ValueTask> unsubscribe
    )
    {
        return new(unsubscribe)
        {
            MessageName = messageName,
            Identity = identity,
            HandlerId = handlerId,
            SubscriptionId = subscriptionId,
            IsAttached = true,
        };
    }

    /// <summary>
    /// Gets the runtime subscription id when the handler is attached.
    /// </summary>
    public string? SubscriptionId { get; private init; }

    /// <summary>
    /// Gets the resolved message name for the runtime handler.
    /// </summary>
    public string MessageName { get; private init; } = string.Empty;

    /// <summary>
    /// Gets the resolved consumer identity of the runtime handler, which is also its Bus subscription name.
    /// </summary>
    public string Identity { get; private init; } = string.Empty;

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
