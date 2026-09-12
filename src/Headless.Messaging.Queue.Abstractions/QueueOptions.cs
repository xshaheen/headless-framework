// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Configures a point-to-point (queue) enqueue operation with delivery behavior, explicit message name,
/// correlation, custom headers, and an optional delivery delay.
/// </summary>
/// <remarks>
/// <para>
/// Accepted by <see cref="IQueue"/>. The invoked queue verb fixes the Queue lane;
/// <see cref="MessageOptions.DeliveryMode"/> controls durability independently.
/// </para>
/// <para>
/// This type is a record so middleware can mutate a single property via a <c>with</c> expression
/// without manually copying every other property. Equality is value-based across every scalar
/// property; <see cref="MessageOptions.Headers"/> uses structural comparison.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record QueueOptions : MessageOptions;
