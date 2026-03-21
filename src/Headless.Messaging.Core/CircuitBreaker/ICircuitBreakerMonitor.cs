// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Read-only view of circuit breaker state for observability and health checks.
/// Also provides operator-level control for manual recovery.
/// </summary>
public interface ICircuitBreakerMonitor
{
    /// <summary>
    /// Returns <c>true</c> if the circuit for the specified consumer group is currently
    /// Open or HalfOpen (i.e., the group is paused or probing).
    /// </summary>
    bool IsOpen(string groupName);

    /// <summary>
    /// Returns the current <see cref="CircuitBreakerState"/> for the specified consumer group,
    /// or <see langword="null"/> if the group is not registered.
    /// </summary>
    CircuitBreakerState? GetState(string groupName);

    /// <summary>
    /// Returns a snapshot of current circuit breaker states for all tracked consumer groups.
    /// The returned list is materialized at call time and safe to hold across async boundaries.
    /// </summary>
    IReadOnlyList<KeyValuePair<string, CircuitBreakerState>> GetAllStates();

    /// <summary>
    /// Returns the set of consumer group names registered via
    /// <see cref="ICircuitBreakerStateManager.RegisterKnownGroups"/>, or <see langword="null"/>
    /// if <c>RegisterKnownGroups</c> was not called. Useful for agents and health-check
    /// endpoints that need to enumerate valid group names before any messages are processed.
    /// </summary>
    IReadOnlySet<string>? KnownGroups { get; }

    /// <summary>
    /// Force-resets the circuit for the specified consumer group to <see cref="CircuitBreakerState.Closed"/>,
    /// cancelling any open timer and resetting escalation. Invokes the resume callback if the circuit
    /// was previously Open or HalfOpen. This is the operator/agent manual recovery path.
    /// </summary>
    /// <param name="groupName">The consumer group name.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if a reset was performed (the group was found and was Open or HalfOpen);
    /// <see langword="false"/> if the group was not found or was already Closed.
    /// </returns>
    ValueTask<bool> ResetAsync(string groupName, CancellationToken cancellationToken = default);
}
