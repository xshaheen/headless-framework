// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Configures a broadcast (bus) publish enlisted in the caller's unit of work, with explicit message name,
/// correlation, custom headers, and an optional delivery delay.
/// </summary>
/// <remarks>
/// <para>
/// This record carries no delivery mode. Durable capture is the enlistment mechanism on this surface, so the
/// choice is made by the receiver rather than by an option: reaching the outbox already selects durable delivery.
/// <see cref="PublishOptions"/> is the counterpart for an autonomous publish that carries the mode.
/// </para>
/// <para>
/// This type is a record so publish-side middleware can mutate a single property via a <c>with</c>
/// expression (for example, <c>options with { TenantId = "acme" }</c>) without manually copying
/// every other property. Equality is value-based across all scalar properties; <see cref="MessageOptions.Headers"/>
/// uses structural comparison (key/value sequence with <see cref="StringComparer.Ordinal"/> on keys).
/// </para>
/// </remarks>
[PublicAPI]
public sealed record OutboxPublishOptions : MessageOptions;
