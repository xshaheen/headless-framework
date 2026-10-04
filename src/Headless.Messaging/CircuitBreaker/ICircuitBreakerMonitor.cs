// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Observability and operator-control surface for the per-consumer circuit breaker.
/// Exposes read access to circuit state and manual recovery actions (reset and force-open).
/// </summary>
/// <remarks>
/// This is the public-facing interface for circuit breaker observability and operator control.
/// The internal <c>ICircuitBreakerStateManager</c> extends this interface with pipeline-internal
/// mutation methods (failure reporting, consumer registration, etc.) not intended for application code.
/// The split is intentional: application code injects <see cref="ICircuitBreakerMonitor"/>
/// to observe state and trigger manual recovery without access to the internal write surface.
/// </remarks>
[PublicAPI]
public interface ICircuitBreakerMonitor
{
    /// <summary>
    /// Returns <see langword="true"/> if the circuit for the specified consumer is currently
    /// Open or HalfOpen (i.e., the consumer is paused or probing).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Advisory hint only:</strong> This method returns <see langword="false"/> for unknown consumers
    /// (consumers not yet registered or never accessed). Use this when you want to check if a
    /// <em>registered</em> consumer is in an open state, not to determine whether a consumer exists.
    /// </para>
    /// <example>
    /// <code>
    /// // Check if a registered consumer is paused/probing
    /// if (monitor.IsOpen(MessageLane.Bus, "payments"))
    /// {
    ///     // Handle paused consumer
    /// }
    ///
    /// // To distinguish between "not open" and "not registered", use GetState:
    /// var state = monitor.GetState(MessageLane.Bus, "payments");
    /// if (state == null)
    /// {
    ///     // Consumer is not registered
    /// }
    /// else if (state == CircuitBreakerState.Open || state == CircuitBreakerState.HalfOpen)
    /// {
    ///     // Consumer is registered and open
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    bool IsOpen(string consumerKey);

    /// <summary>
    /// Returns <see langword="true"/> if the circuit for the specified delivery intent and consumer
    /// is currently Open or HalfOpen.
    /// </summary>
    /// <param name="lane">The delivery intent (<see cref="MessageLane.Bus"/> or <see cref="MessageLane.Queue"/>).</param>
    /// <param name="consumerIdentity">The consumer identity.</param>
    /// <returns><see langword="true"/> when the circuit is Open or HalfOpen; <see langword="false"/> when Closed or not registered.</returns>
    bool IsOpen(MessageLane lane, string consumerIdentity);

    /// <summary>
    /// Returns the current <see cref="CircuitBreakerState"/> for the specified consumer,
    /// or <see langword="null"/> if the consumer is not registered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Registration check:</strong> Unlike <see cref="IsOpen(string)"/>, this method distinguishes
    /// between "not registered" (<see langword="null"/>) and "registered but closed/open"
    /// (returns <see cref="CircuitBreakerState"/>). Use this when you need to verify whether
    /// a consumer is actually registered before making decisions.
    /// </para>
    /// <example>
    /// <code>
    /// // Get precise state information
    /// var state = monitor.GetState(MessageLane.Bus, "payments");
    ///
    /// if (state == null)
    /// {
    ///     // Consumer has never been registered or accessed
    ///     return;
    /// }
    ///
    /// // Consumer is registered; check its state
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
    CircuitBreakerState? GetState(string consumerKey);

    /// <summary>
    /// Returns the current <see cref="CircuitBreakerState"/> for the specified delivery intent and consumer,
    /// or <see langword="null"/> if the consumer is not registered.
    /// </summary>
    /// <param name="lane">The delivery intent (<see cref="MessageLane.Bus"/> or <see cref="MessageLane.Queue"/>).</param>
    /// <param name="consumerIdentity">The consumer identity.</param>
    /// <returns>The current state, or <see langword="null"/> when the consumer has never been registered.</returns>
    CircuitBreakerState? GetState(MessageLane lane, string consumerIdentity);

    /// <summary>
    /// Returns a snapshot of current circuit breaker states for all tracked consumers.
    /// The returned dictionary is materialized at call time and safe to hold across async boundaries.
    /// </summary>
    IReadOnlyDictionary<string, CircuitBreakerState> GetAllStates();

    /// <summary>
    /// Gets a rich snapshot of the circuit breaker state for a consumer.
    /// </summary>
    /// <param name="consumerKey">
    /// The lane-qualified circuit key, as listed by <see cref="KnownConsumers"/> and <see cref="GetAllStates"/>.
    /// </param>
    /// <returns>The snapshot, or <see langword="null"/> if the consumer has not been accessed.</returns>
    CircuitBreakerSnapshot? GetSnapshot(string consumerKey);

    /// <summary>
    /// Gets a rich snapshot of the circuit breaker state for a delivery intent and consumer.
    /// </summary>
    /// <param name="lane">The delivery intent (<see cref="MessageLane.Bus"/> or <see cref="MessageLane.Queue"/>).</param>
    /// <param name="consumerIdentity">The consumer identity.</param>
    /// <returns>The snapshot, or <see langword="null"/> if the consumer has not been accessed.</returns>
    CircuitBreakerSnapshot? GetSnapshot(MessageLane lane, string consumerIdentity);

    /// <summary>
    /// Returns the lane-qualified circuit keys of the consumers registered via
    /// <see cref="ICircuitBreakerStateManager.RegisterKnownConsumers"/>. Returns an empty set
    /// if <c>RegisterKnownConsumers</c> has not been called yet. Useful for agents and health-check
    /// endpoints that need to enumerate valid consumer names before any messages are processed.
    /// </summary>
    IReadOnlySet<string> KnownConsumers { get; }

    /// <summary>
    /// Force-resets the circuit for the specified consumer to <see cref="CircuitBreakerState.Closed"/>,
    /// cancelling any open timer and resetting the escalation level to zero. Invokes the resume callback
    /// if the circuit was previously Open or HalfOpen. This is the operator/agent manual recovery path.
    /// </summary>
    /// <param name="consumerKey">
    /// The lane-qualified circuit key, as listed by <see cref="KnownConsumers"/> and <see cref="GetAllStates"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Observed only before the state transition begins. Once the reset starts it is must-complete —
    /// the token is not observed mid-transition or while awaiting the resume callback, to avoid leaving
    /// the breaker in a torn (half-applied) state.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a reset was performed (the consumer was found and was Open or HalfOpen);
    /// <see langword="false"/> if the consumer was not found or was already Closed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Authorization required:</strong> This method resumes all message consumption for the consumer.
    /// HTTP or gRPC endpoints that expose this operation MUST require authorization to prevent
    /// denial-of-service attacks where an unauthenticated caller prematurely re-opens a closed circuit.
    /// </para>
    /// </remarks>
    ValueTask<bool> ResetAsync(string consumerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Force-resets the circuit for the specified delivery intent and consumer to <see cref="CircuitBreakerState.Closed"/>.
    /// </summary>
    /// <param name="lane">The delivery intent (<see cref="MessageLane.Bus"/> or <see cref="MessageLane.Queue"/>).</param>
    /// <param name="consumerIdentity">The consumer identity.</param>
    /// <param name="cancellationToken">
    /// Observed only before the state transition begins. Once the reset starts it is must-complete —
    /// the token is not observed mid-transition or while awaiting the resume callback, to avoid leaving
    /// the breaker in a torn (half-applied) state.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a reset was performed; <see langword="false"/> if the consumer was not found or was already Closed.
    /// </returns>
    ValueTask<bool> ResetAsync(
        MessageLane lane,
        string consumerIdentity,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Force-opens the circuit for the specified consumer, transitioning it to
    /// <see cref="CircuitBreakerState.Open"/> and invoking the pause callback. Does not
    /// increment escalation level (forced opens bypass natural failure counting).
    /// </summary>
    /// <param name="consumerKey">
    /// The lane-qualified circuit key, as listed by <see cref="KnownConsumers"/> and <see cref="GetAllStates"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Observed only before the state transition begins. Once the force-open starts it is must-complete —
    /// the token is not observed mid-transition or while awaiting the pause callback, to avoid leaving
    /// the breaker in a torn (half-applied) state.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the circuit was force-opened (consumer was found and was Closed or HalfOpen);
    /// <see langword="false"/> if the consumer was not found or was already Open.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Authorization required:</strong> This method halts all message consumption for the consumer.
    /// HTTP or gRPC endpoints that expose this operation MUST require authorization to prevent
    /// denial-of-service attacks where an unauthenticated caller can arbitrarily pause consumers.
    /// </para>
    /// </remarks>
    ValueTask<bool> ForceOpenAsync(string consumerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Force-opens the circuit for the specified delivery intent and consumer, transitioning it to
    /// <see cref="CircuitBreakerState.Open"/> and invoking the pause callback.
    /// </summary>
    /// <param name="lane">The delivery intent (<see cref="MessageLane.Bus"/> or <see cref="MessageLane.Queue"/>).</param>
    /// <param name="consumerIdentity">The consumer identity.</param>
    /// <param name="cancellationToken">
    /// Observed only before the state transition begins. Once the force-open starts it is must-complete —
    /// the token is not observed mid-transition or while awaiting the pause callback, to avoid leaving
    /// the breaker in a torn (half-applied) state.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the circuit was force-opened; <see langword="false"/> if the consumer was not found or was already Open.
    /// </returns>
    ValueTask<bool> ForceOpenAsync(
        MessageLane lane,
        string consumerIdentity,
        CancellationToken cancellationToken = default
    );
}
