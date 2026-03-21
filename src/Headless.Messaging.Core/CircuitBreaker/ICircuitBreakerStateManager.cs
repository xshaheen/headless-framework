// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Manages per-consumer-group circuit breaker state, tracking failure rates and coordinating
/// Open/HalfOpen/Closed transitions. Intended as an internal singleton service.
/// </summary>
internal interface ICircuitBreakerStateManager : ICircuitBreakerMonitor
{
    /// <summary>
    /// Registers pause and resume callbacks for a consumer group.
    /// The <paramref name="onPause"/> callback is invoked when the circuit opens;
    /// <paramref name="onResume"/> is invoked when the circuit transitions to half-open.
    /// </summary>
    /// <param name="groupName">The consumer group name.</param>
    /// <param name="onPause">Invoked when the circuit transitions to <see cref="CircuitBreakerState.Open"/>.</param>
    /// <param name="onResume">Invoked when the circuit transitions to <see cref="CircuitBreakerState.HalfOpen"/>.</param>
    void RegisterGroupCallbacks(string groupName, Func<ValueTask> onPause, Func<ValueTask> onResume);

    /// <summary>
    /// Reports a failure for the specified consumer group. If the exception is transient and
    /// the failure threshold is reached, the circuit will open and <c>onPause</c> will be invoked.
    /// </summary>
    /// <param name="groupName">The consumer group name.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    ValueTask ReportFailureAsync(string groupName, Exception exception);

    /// <summary>
    /// Attempts to acquire the single HalfOpen probe slot for the specified group.
    /// Returns <see langword="true"/> when the group is not HalfOpen, or when the probe
    /// slot was acquired successfully.
    /// </summary>
    /// <param name="groupName">The consumer group name.</param>
    bool TryAcquireHalfOpenProbe(string groupName);

    /// <summary>
    /// Releases a previously acquired HalfOpen probe slot without changing circuit state.
    /// Intended for failures that occur before a probe reaches the normal success/failure
    /// reporting path.
    /// </summary>
    /// <param name="groupName">The consumer group name.</param>
    void ReleaseHalfOpenProbe(string groupName);

    /// <summary>
    /// Reports a successful message processing for the specified consumer group.
    /// Resets the consecutive failure counter and, if in half-open state, closes the circuit.
    /// </summary>
    /// <param name="groupName">The consumer group name.</param>
    void ReportSuccess(string groupName);

    /// <summary>
    /// Removes all tracked state for the specified group, including timers and callbacks.
    /// Intended for consumer teardown/restart paths.
    /// </summary>
    /// <param name="groupName">The consumer group name.</param>
    void RemoveGroup(string groupName);
}
