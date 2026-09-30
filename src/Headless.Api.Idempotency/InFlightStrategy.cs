// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Idempotency;

/// <summary>How a request is answered while another attempt with the same idempotency key is still running.</summary>
[PublicAPI]
public enum InFlightStrategy
{
    /// <summary>
    /// Return 409 Conflict (<c>g:idempotency_in_flight</c>) at once, so the client retries after a backoff. Default.
    /// </summary>
    Reject = 0,

    /// <summary>
    /// Poll admission with a bounded backoff until the running attempt completes (replay its response), gives the key
    /// up or loses its lease (run the handler as the new owner), or
    /// <see cref="IdempotencyOptions.InFlightLockTimeout" /> elapses (409 <c>g:idempotency_in_flight_timeout</c>).
    /// </summary>
    WaitAndReplay = 1,
}
