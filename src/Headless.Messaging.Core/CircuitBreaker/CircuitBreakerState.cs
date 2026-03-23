// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Represents the state of a circuit breaker for a messaging consumer group.
/// </summary>
public enum CircuitBreakerState
{
    /// <summary>
    /// The circuit is closed and messages are flowing normally.
    /// </summary>
    Closed = 0,

    /// <summary>
    /// The circuit is open and messages are being rejected to protect the downstream dependency.
    /// </summary>
    Open = 1,

    /// <summary>
    /// The circuit is half-open, allowing a single probe message through to test if the dependency has recovered.
    /// </summary>
    HalfOpen = 2,
}
