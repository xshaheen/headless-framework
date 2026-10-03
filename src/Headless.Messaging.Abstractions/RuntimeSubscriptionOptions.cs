// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Options used when attaching a runtime message handler to the broker subscription pipeline.
/// </summary>
[PublicAPI]
public sealed class RuntimeSubscriptionOptions
{
    /// <summary>
    /// Gets or sets the explicit message name to subscribe to.
    /// When omitted, the message name is resolved from configured mappings or deterministic conventions for the message type.
    /// </summary>
    public string? MessageName { get; init; }

    /// <summary>
    /// Gets or sets the subscription's consumer identity, which is also its Bus subscription name: runtime subscriptions
    /// with the same identity compete for each message, in whatever process attaches them.
    /// When omitted, the identity is derived from <see cref="HandlerId"/>.
    /// </summary>
    public string? Identity { get; init; }

    /// <summary>
    /// Gets or sets the concurrency limit for the runtime handler.
    /// Defaults to <c>1</c>.
    /// </summary>
    public byte Concurrency { get; init; } = 1;

    /// <summary>
    /// Gets or sets the deterministic identity of the handler delegate, used for diagnostics, duplicate detection, and the
    /// default <see cref="Identity"/>.
    /// This is required for anonymous or compiler-generated delegates because the default policy fails fast when the identity is not deterministic.
    /// </summary>
    public string? HandlerId { get; init; }

    /// <summary>
    /// Gets or sets how duplicate runtime registrations are handled.
    /// Defaults to <see cref="RuntimeSubscriptionDuplicateBehavior.Reject" /> so duplicate runtime registrations fail fast unless an explicit opt-out is chosen.
    /// </summary>
    public RuntimeSubscriptionDuplicateBehavior DuplicateBehavior { get; init; } =
        RuntimeSubscriptionDuplicateBehavior.Reject;

    /// <summary>
    /// Gets or sets whether this process receives every published message through a subscription of its own, instead of
    /// competing with the other processes that attach the same handler. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Delivery is at most once and only while the subscription is attached: there is no backlog, no inbox row, and no
    /// retry, and a handler failure is logged and the message dropped. The transport must support every-instance
    /// subscriptions. The per-process subscription name is derived from <see cref="Identity"/>.
    /// </remarks>
    public bool EveryInstance { get; init; }
}
