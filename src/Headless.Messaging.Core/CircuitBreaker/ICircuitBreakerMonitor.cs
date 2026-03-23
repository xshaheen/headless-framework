// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Observability and operator-control surface for the per-group circuit breaker.
/// Exposes read access to circuit state and manual recovery actions (reset and force-open).
/// </summary>
/// <remarks>
/// This is the public-facing interface for circuit breaker observability and operator control.
/// The internal <c>ICircuitBreakerStateManager</c> extends this interface with pipeline-internal
/// mutation methods (failure reporting, group registration, etc.) not intended for application code.
/// The split is intentional: application code injects <see cref="ICircuitBreakerMonitor"/>
/// to observe state and trigger manual recovery without access to the internal write surface.
/// </remarks>
public interface ICircuitBreakerMonitor
{
    /// <summary>
    /// Returns <c>true</c> if the circuit for the specified consumer group is currently
    /// Open or HalfOpen (i.e., the group is paused or probing).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Advisory hint only:</strong> This method returns <c>false</c> for unknown groups
    /// (groups not yet registered or never accessed). Use this when you want to check if a
    /// <em>registered</em> group is in an open state, not to determine whether a group exists.
    /// </para>
    /// <example>
    /// <code>
    /// // Check if a registered group is paused/probing
    /// if (monitor.IsOpen("payments"))
    /// {
    ///     // Handle paused consumer group
    /// }
    ///
    /// // To distinguish between "not open" and "not registered", use GetState:
    /// var state = monitor.GetState("payments");
    /// if (state == null)
    /// {
    ///     // Group is not registered
    /// }
    /// else if (state == CircuitBreakerState.Open || state == CircuitBreakerState.HalfOpen)
    /// {
    ///     // Group is registered and open
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    bool IsOpen(string groupName);

    /// <summary>
    /// Returns the current <see cref="CircuitBreakerState"/> for the specified consumer group,
    /// or <see langword="null"/> if the group is not registered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Registration check:</strong> Unlike <see cref="IsOpen"/>, this method distinguishes
    /// between "not registered" (<see langword="null"/>) and "registered but closed/open"
    /// (returns <see cref="CircuitBreakerState"/>). Use this when you need to verify whether
    /// a group is actually registered before making decisions.
    /// </para>
    /// <example>
    /// <code>
    /// // Get precise state information
    /// var state = monitor.GetState("payments");
    ///
    /// if (state == null)
    /// {
    ///     // Group has never been registered or accessed
    ///     return;
    /// }
    ///
    /// // Group is registered; check its state
    /// switch (state)
    /// {
    ///     case CircuitBreakerState.Closed:
    ///         // Healthy, processing normally
    ///         break;
    ///     case CircuitBreakerState.Open:
    ///     case CircuitBreakerState.HalfOpen:
    ///         // Paused or probing for recovery
    ///         break;
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    CircuitBreakerState? GetState(string groupName);

    /// <summary>
    /// Returns a snapshot of current circuit breaker states for all tracked consumer groups.
    /// The returned list is materialized at call time and safe to hold across async boundaries.
    /// </summary>
    IReadOnlyList<KeyValuePair<string, CircuitBreakerState>> GetAllStates();

    /// <summary>
    /// Gets a rich snapshot of the circuit breaker state for a consumer group.
    /// </summary>
    /// <param name="groupName">The consumer group name.</param>
    /// <returns>The snapshot, or <see langword="null"/> if the group has not been accessed.</returns>
    CircuitBreakerSnapshot? GetSnapshot(string groupName);

    /// <summary>
    /// Returns the set of consumer group names registered via
    /// <see cref="ICircuitBreakerStateManager.RegisterKnownGroups"/>. Returns an empty set
    /// if <c>RegisterKnownGroups</c> has not been called yet. Useful for agents and health-check
    /// endpoints that need to enumerate valid group names before any messages are processed.
    /// </summary>
    IReadOnlySet<string> KnownGroups { get; }

    /// <summary>
    /// Force-resets the circuit for the specified consumer group to <see cref="CircuitBreakerState.Closed"/>,
    /// cancelling any open timer and resetting the escalation level to zero. Invokes the resume callback
    /// if the circuit was previously Open or HalfOpen. This is the operator/agent manual recovery path.
    /// </summary>
    /// <param name="groupName">The consumer group name.</param>
    /// <returns>
    /// <see langword="true"/> if a reset was performed (the group was found and was Open or HalfOpen);
    /// <see langword="false"/> if the group was not found or was already Closed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Authorization required:</strong> This method resumes all message consumption for the group.
    /// HTTP or gRPC endpoints that expose this operation MUST require authorization to prevent
    /// denial-of-service attacks where an unauthenticated caller prematurely re-opens a closed circuit.
    /// </para>
    /// </remarks>
    ValueTask<bool> ResetAsync(string groupName);

    /// <summary>
    /// Force-opens the circuit for the specified consumer group, transitioning it to
    /// <see cref="CircuitBreakerState.Open"/> and invoking the pause callback. Does not
    /// increment escalation level (forced opens bypass natural failure counting).
    /// </summary>
    /// <param name="groupName">The consumer group name.</param>
    /// <returns>
    /// <see langword="true"/> if the circuit was force-opened (group was found and was Closed or HalfOpen);
    /// <see langword="false"/> if the group was not found or was already Open.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Authorization required:</strong> This method halts all message consumption for the group.
    /// HTTP or gRPC endpoints that expose this operation MUST require authorization to prevent
    /// denial-of-service attacks where an unauthenticated caller can arbitrarily pause consumers.
    /// </para>
    /// </remarks>
    ValueTask<bool> ForceOpenAsync(string groupName);
}
