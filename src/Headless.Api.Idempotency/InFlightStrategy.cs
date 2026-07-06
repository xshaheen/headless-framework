// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Idempotency;

/// <summary>How concurrent requests sharing the same idempotency key are resolved.</summary>
[PublicAPI]
public enum InFlightStrategy
{
    /// <summary>
    /// Return 409 Conflict (<c>g:idempotency_in_flight</c>) immediately when a request with the
    /// same key is already executing. No distributed lock is required; this is the default.
    /// </summary>
    Reject = 0,

    /// <summary>
    /// Acquire a distributed lock keyed by the cache slot, block waiting for the winner to
    /// finalize, and replay the cached response. Requires an <c>IDistributedLock</c>
    /// to be registered (enforced at startup). If the acquisition timeout elapses, returns
    /// 409 with <c>g:idempotency_in_flight_timeout</c>.
    /// </summary>
    WaitAndReplay = 1,
}
