// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Configures a broadcast (bus) publish operation with delivery behavior, explicit message name,
/// correlation, custom headers, and an optional delivery delay.
/// </summary>
/// <remarks>
/// <para>
/// Accepted by <see cref="IBus"/>, which publishes autonomously and never joins the caller's transaction, so
/// <see cref="DeliveryMode"/> is a free choice here. <see cref="OutboxPublishOptions"/> is the counterpart for
/// an enlisted publish and carries no mode.
/// </para>
/// <para>
/// This type is a record so publish-side middleware can mutate a single property via a <c>with</c>
/// expression (for example, <c>options with { TenantId = "acme" }</c>) without manually copying
/// every other property. Equality is value-based across all scalar properties; <see cref="Headers"/>
/// uses structural comparison (key/value sequence with <see cref="StringComparer.Ordinal"/> on keys).
/// </para>
/// <para>
/// Two <see cref="PublishOptions"/> instances are equal when every scalar field matches and their
/// <see cref="Headers"/> dictionaries contain the same key/value pairs — independent of the
/// underlying dictionary instance, ordering, or comparer. This avoids the reference-equality footgun
/// that the synthesized record equality would otherwise introduce on the dictionary-typed property.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record PublishOptions : MessageOptions
{
    /// <summary>
    /// Gets the per-call delivery override. Null inherits the per-type registration, then the host default,
    /// which is <see cref="DeliveryMode.Durable"/> unless <c>MessagingOptions.DefaultDeliveryMode</c> selects
    /// another mode.
    /// </summary>
    public DeliveryMode? DeliveryMode { get; init; }
}
