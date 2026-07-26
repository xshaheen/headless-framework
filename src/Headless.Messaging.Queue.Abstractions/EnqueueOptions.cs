// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Configures a point-to-point (queue) enqueue operation with delivery behavior, explicit message name,
/// correlation, custom headers, and an optional delivery delay.
/// </summary>
/// <remarks>
/// <para>
/// Accepted by <see cref="IQueue.EnqueueAsync{T}"/>. The invoked queue verb fixes the Queue lane;
/// <see cref="MessageOptions.DeliveryMode"/> controls durability independently.
/// </para>
/// <para>
/// This type is a record so middleware can mutate a single property via a <c>with</c> expression
/// without manually copying every other property. Equality is value-based across every scalar
/// property; <see cref="MessageOptions.Headers"/> uses structural comparison.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record EnqueueOptions : MessageOptions
{
    /// <summary>
    /// Gets the relative delay applied before the durably captured message is dispatched.
    /// </summary>
    /// <remarks>
    /// A delay requires durable delivery. With <see cref="DeliveryMode.Auto"/> it selects durable capture;
    /// with <see cref="DeliveryMode.TransportDirect"/> the operation is rejected.
    /// </remarks>
    public TimeSpan? Delay { get; init; }

    /// <summary>
    /// Determines whether the specified <see cref="EnqueueOptions"/> equals this instance using
    /// value semantics across every scalar field plus structural comparison on
    /// <see cref="MessageOptions.Headers"/>.
    /// </summary>
    public bool Equals(EnqueueOptions? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return base.Equals(other) && Nullable.Equals(Delay, other.Delay);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(base.GetHashCode(), Delay);
    }
}
