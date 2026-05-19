// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Idempotency;

/// <summary>How request bodies exceeding <c>IdempotencyOptions.MaxBodySizeForHashing</c> are handled.</summary>
[PublicAPI]
public enum OversizeBehavior
{
    /// <summary>
    /// Return 413 Payload Too Large (<c>g:idempotency_body_too_large</c>) without invoking the
    /// downstream handler. Default for endpoints where unbounded inputs are unexpected.
    /// </summary>
    Reject = 0,

    /// <summary>
    /// Pass the request through to the downstream handler without applying idempotency
    /// guarantees (no fingerprinting, no replay, no marker insert). Choose this when oversize
    /// payloads are legitimate and replay is not required for them.
    /// </summary>
    PassThrough = 1,
}
