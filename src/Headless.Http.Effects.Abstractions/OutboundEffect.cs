// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Http.Effects;

/// <summary>
/// The side-effect class of one outbound provider's HTTP operations, from which the resilience
/// pipeline is derived.
/// </summary>
/// <remarks>
/// <para>
/// A provider declares the strongest guarantee its API actually offers — the declaration is
/// checked against what the provider's wire contract supports, not what would be convenient.
/// The derived pipeline is: <see cref="Safe"/> retries every method (reads);
/// <see cref="Idempotent"/> retries every method with a stable idempotency key attached per
/// logical call; <see cref="Unsafe"/> never retries automatically — the caller owns any retry,
/// because a re-sent request may take effect twice.
/// </para>
/// <para>
/// This is the outbound counterpart of the gRPC service-config retry policy (gRFC A6, where only
/// methods declared eligible are retried) and of Stripe's <c>Idempotency-Key</c> semantics.
/// </para>
/// </remarks>
[PublicAPI]
public enum OutboundEffect
{
    /// <summary>
    /// A failed or timed-out request has no lasting side effect (reads, queries). The derived
    /// pipeline retries every HTTP method on transient failures.
    /// </summary>
    Safe = 0,

    /// <summary>
    /// A retry is only harmless when the provider can deduplicate it, so every retried request
    /// carries a stable key for the duration of one logical call. The derived pipeline retries
    /// with an <c>Idempotency-Key</c>-style header when the provider supports one.
    /// </summary>
    Idempotent = 1,

    /// <summary>
    /// A re-sent mutating request may take effect twice (payments, message sends). The derived
    /// pipeline disables automatic retry for the RFC-unsafe methods (POST, PUT, PATCH, DELETE,
    /// CONNECT); callers that want retry on those must key the call themselves and opt back in
    /// explicitly. Reads through the same client stay retryable.
    /// </summary>
    Unsafe = 2,
}
