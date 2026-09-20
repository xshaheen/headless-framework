// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Configures a point-to-point (queue) enqueue enlisted in the caller's unit of work, with explicit message name,
/// correlation, custom headers, and an optional delivery delay.
/// </summary>
/// <remarks>
/// <para>
/// This record carries no delivery mode. Durable capture is the enlistment mechanism on this surface, so the
/// choice is made by the receiver rather than by an option: reaching the outbox already selects durable delivery.
/// <see cref="QueueOptions"/> is the counterpart for an autonomous enqueue that carries the mode.
/// </para>
/// <para>
/// This type is a record so middleware can mutate a single property via a <c>with</c> expression
/// without manually copying every other property. Equality is value-based across every scalar
/// property; <see cref="MessageOptions.Headers"/> uses structural comparison.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record OutboxQueueOptions : MessageOptions;
