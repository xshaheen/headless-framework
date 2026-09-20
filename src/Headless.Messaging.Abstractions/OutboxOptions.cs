// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Configures a publish or enqueue enlisted in the caller's unit of work through <c>unit.Outbox</c>, with
/// explicit message name, correlation, custom headers, and an optional delivery delay or schedule.
/// </summary>
/// <remarks>
/// <para>
/// One record serves both lanes because the verb is the lane authority: <c>PublishAsync</c> broadcasts on the
/// bus, <c>EnqueueAsync</c> enqueues point-to-point, and nothing in the options changes that. The record carries
/// no delivery mode either: durable capture is how an enlisted write joins the transaction, so reaching the outbox
/// already selects durable delivery. <c>PublishOptions</c> and <c>QueueOptions</c> are the
/// autonomous counterparts that carry the mode.
/// </para>
/// <para>
/// This type is a record so publish-side middleware can mutate a single property via a <c>with</c> expression
/// (for example, <c>options with { TenantId = "acme" }</c>) without manually copying every other property.
/// Equality is value-based across all scalar properties; <see cref="MessageOptions.Headers" /> uses structural
/// comparison (key/value sequence with <see cref="StringComparer.Ordinal" /> on keys).
/// </para>
/// </remarks>
[PublicAPI]
public sealed record OutboxOptions : MessageOptions;
