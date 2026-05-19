// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Idempotency;

/// <summary>
/// Behavior when the underlying <see cref="Headless.Caching.ICache"/> throws during an idempotency
/// middleware operation (read, sentinel insert, finalize).
/// </summary>
[PublicAPI]
public enum OnCacheErrorBehavior
{
    /// <summary>
    /// Log a warning and bypass idempotency for the failing request: pass through to the
    /// inner pipeline (pre-handler sites) or return a recoverable 409
    /// <c>g:idempotency_in_flight_timeout</c> (loser sites). Trades the idempotency
    /// guarantee for the failing request against an outage-wide 5xx storm. Default.
    /// </summary>
    /// <remarks>
    /// Under <see cref="FailOpen"/> a single client retry may execute its handler twice if the
    /// cache outage straddles two attempts. This matches Stripe/AWS/Square behavior — operators
    /// who prefer to surface the outage instead should choose <see cref="Throw"/>.
    /// </remarks>
    FailOpen = 0,

    /// <summary>
    /// Propagate the cache exception to the host pipeline. Useful in strict environments
    /// (tests, regulated workloads) that prefer 5xx over silently dropping the idempotency
    /// guarantee.
    /// </summary>
    Throw = 1,
}
