// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Configures a point-to-point (queue) enqueue operation with delivery behavior, explicit message name,
/// correlation, custom headers, and an optional delivery delay.
/// </summary>
/// <remarks>
/// <para>
/// Accepted by <see cref="IQueue"/>, which enqueues autonomously and never joins the caller's transaction. The
/// invoked queue verb fixes the Queue lane; <see cref="DeliveryMode"/> controls durability independently.
/// <see cref="OutboxOptions"/> is the counterpart for an enlisted enqueue and carries no mode.
/// </para>
/// <para>
/// This type is a record so middleware can mutate a single property via a <c>with</c> expression
/// without manually copying every other property. Equality is value-based across every scalar
/// property; <see cref="MessageOptions.Headers"/> uses structural comparison.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record QueueOptions : MessageOptions
{
    /// <summary>
    /// Gets the per-call delivery override. Null inherits the per-type registration, then the host default,
    /// which is <see cref="DeliveryMode.Durable"/> unless <c>MessagingOptions.DefaultDeliveryMode</c> selects
    /// another mode.
    /// </summary>
    public DeliveryMode? DeliveryMode { get; init; }
}
