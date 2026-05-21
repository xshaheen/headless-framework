// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Configures a broadcast (bus) publish operation with explicit topic, correlation, custom header,
/// and (for outbox-backed publishes) delivery-delay overrides.
/// </summary>
/// <remarks>
/// <para>
/// Accepted by <see cref="IBus.PublishAsync{T}"/> and <see cref="IOutboxBus.PublishAsync{T}"/>.
/// <see cref="Delay"/> is honored only when the publish is persisted through the outbox
/// (<see cref="IOutboxBus"/>); direct publishers (<see cref="IBus"/>) ignore it.
/// </para>
/// <para>
/// This type is a record so middleware can mutate a single property via a <c>with</c> expression
/// without manually copying every other property. Equality is value-based across every scalar
/// property; <see cref="MessagePublishOptionsBase.Headers"/> uses structural comparison.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record PublishOptions : MessagePublishOptionsBase
{
    /// <summary>
    /// Gets the delay applied before the persisted message is dispatched.
    /// </summary>
    /// <remarks>
    /// Honored only by <see cref="IOutboxBus"/>. Ignored by the fire-and-forget <see cref="IBus"/>
    /// interface (whose contract is immediate broker-side publish with no persistence).
    /// </remarks>
    public TimeSpan? Delay { get; init; }

    /// <summary>
    /// Determines whether the specified <see cref="PublishOptions"/> equals this instance using
    /// value semantics across every scalar field plus structural comparison on
    /// <see cref="MessagePublishOptionsBase.Headers"/>.
    /// </summary>
    public bool Equals(PublishOptions? other)
    {
        return base.Equals(other) && Nullable.Equals(Delay, other?.Delay);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(base.GetHashCode(), Delay);
    }
}
