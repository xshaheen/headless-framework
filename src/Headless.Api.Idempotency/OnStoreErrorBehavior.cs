// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Idempotency;

/// <summary>
/// Behavior when the durable idempotency store (<c>IIdempotentOperations</c>) throws before the handler runs, or while
/// a waiting request polls for the in-flight attempt's result.
/// </summary>
/// <remarks>
/// A completion or release that fails after the handler's response has started is always logged and never thrown,
/// whatever this setting says: the client already has its response, and throwing would only surface a 500 it never
/// sees.
/// </remarks>
[PublicAPI]
public enum OnStoreErrorBehavior
{
    /// <summary>
    /// Propagate the store exception to the host pipeline, so the client gets a 5xx and retries. Default: the store
    /// exists to guarantee one execution per key, and failing open silently drops that guarantee.
    /// </summary>
    Throw = 0,

    /// <summary>
    /// Log a warning and bypass idempotency for the failing request: run the handler unguarded before admission, or
    /// return a recoverable 409 <c>g:idempotency_in_flight_timeout</c> while waiting on another attempt (running the
    /// handler there could execute the operation twice). A retry during a store outage may run its handler twice.
    /// </summary>
    FailOpen = 1,
}
