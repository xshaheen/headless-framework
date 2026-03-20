// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Read-only view of circuit breaker state for observability and health checks.
/// </summary>
public interface ICircuitBreakerMonitor
{
    /// <summary>
    /// Returns <c>true</c> if the circuit for the specified consumer group is currently
    /// Open or HalfOpen (i.e., the group is paused or probing).
    /// </summary>
    bool IsOpen(string groupName);

    /// <summary>
    /// Returns the current <see cref="CircuitBreakerState"/> for the specified consumer group.
    /// Returns <see cref="CircuitBreakerState.Closed"/> for unregistered groups.
    /// </summary>
    CircuitBreakerState GetState(string groupName);
}
