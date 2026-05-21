// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Configures a point-to-point (queue) enqueue operation with explicit topic, correlation, custom
/// header, and (for outbox-backed enqueues) delivery-delay overrides.
/// </summary>
/// <remarks>
/// <para>
/// Accepted by <see cref="IQueue.EnqueueAsync{T}"/> and <see cref="IOutboxQueue.EnqueueAsync{T}"/>.
/// <see cref="Delay"/> is honored only when the enqueue is persisted through the outbox
/// (<see cref="IOutboxQueue"/>); direct enqueuers (<see cref="IQueue"/>) ignore it.
/// </para>
/// <para>
/// This type is a record so middleware can mutate a single property via a <c>with</c> expression
/// without manually copying every other property. Equality is value-based across every scalar
/// property; <see cref="MessagePublishOptionsBase.Headers"/> uses structural comparison.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record EnqueueOptions : MessagePublishOptionsBase
{
    /// <summary>
    /// Gets the delay applied before the persisted message is dispatched.
    /// </summary>
    /// <remarks>
    /// Honored only by <see cref="IOutboxQueue"/>. Ignored by the fire-and-forget <see cref="IQueue"/>
    /// interface (whose contract is immediate broker-side enqueue with no persistence).
    /// </remarks>
    public TimeSpan? Delay { get; init; }

    /// <summary>
    /// Determines whether the specified <see cref="EnqueueOptions"/> equals this instance using
    /// value semantics across every scalar field plus structural comparison on
    /// <see cref="MessagePublishOptionsBase.Headers"/>.
    /// </summary>
    public bool Equals(EnqueueOptions? other)
    {
        return base.Equals(other) && Nullable.Equals(Delay, other.Delay);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(base.GetHashCode(), Delay);
    }
}
