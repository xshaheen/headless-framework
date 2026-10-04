// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Messaging;

/// <summary>
/// Manages per-consumer circuit breaker state, tracking failure rates and coordinating
/// Open/HalfOpen/Closed transitions. Intended as an internal singleton service.
/// </summary>
internal interface ICircuitBreakerStateManager : ICircuitBreakerMonitor
{
    /// <summary>
    /// Atomically classifies a claimed persisted retry against the lane-qualified circuit and,
    /// when eligible, reserves the same HalfOpen probe slot used by transport deliveries.
    /// </summary>
    CircuitRetryDecision GetRetryDecision(MessageLane lane, string consumerIdentity);

    /// <summary>
    /// Freezes the set of valid consumer names. After this call, unrecognized consumer names
    /// receive a no-op circuit state to prevent unbounded OTel cardinality. Should be called once
    /// during startup after all consumers are registered.
    /// </summary>
    /// <param name="consumers">The known consumer names.</param>
    void RegisterKnownConsumers(IEnumerable<string> consumers);

    /// <summary>
    /// Registers pause and resume callbacks for a consumer.
    /// The <paramref name="onPause"/> callback is invoked when the circuit opens;
    /// <paramref name="onResume"/> is invoked when the circuit transitions to half-open.
    /// </summary>
    /// <param name="consumerKey">The consumer name.</param>
    /// <param name="onPause">Invoked when the circuit transitions to <see cref="CircuitBreakerState.Open"/>.</param>
    /// <param name="onResume">Invoked when the circuit transitions to <see cref="CircuitBreakerState.HalfOpen"/>.</param>
    void RegisterConsumerCallbacks(string consumerKey, Func<long, ValueTask> onPause, Func<long, ValueTask> onResume);

    /// <summary>
    /// Reports a failure for the specified consumer. If the exception is transient and
    /// the failure threshold is reached, the circuit will open and <c>onPause</c> will be invoked.
    /// </summary>
    /// <param name="consumerKey">The consumer name.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <param name="cancellationToken">Token to cancel the operation (e.g. transport pause callback).</param>
    ValueTask ReportFailureAsync(
        string consumerKey,
        Exception exception,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Attempts to acquire the single HalfOpen probe slot for the specified consumer.
    /// Returns the current admission epoch when the probe slot was acquired successfully
    /// or when the consumer is not HalfOpen; returns <see langword="null"/> when the probe slot is already held.
    /// </summary>
    /// <param name="consumerKey">The consumer name.</param>
    /// <returns>The admission epoch if admitted, or <see langword="null"/> if the probe is taken.</returns>
    long? TryAcquireHalfOpenProbe(string consumerKey);

    /// <summary>
    /// Releases a previously acquired HalfOpen probe slot without changing circuit state.
    /// Intended for failures that occur before a probe reaches the normal success/failure
    /// reporting path.
    /// </summary>
    /// <param name="consumerKey">The consumer name.</param>
    void ReleaseHalfOpenProbe(string consumerKey, long epoch);

    /// <summary>
    /// Reports a successful message processing for the specified consumer.
    /// Resets the consecutive failure counter and, if in half-open state, closes the circuit.
    /// </summary>
    /// <param name="consumerKey">The consumer name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask ReportSuccessAsync(string consumerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all tracked state for the specified consumer, including timers and callbacks.
    /// Intended for consumer teardown/restart paths.
    /// </summary>
    /// <param name="consumerKey">The consumer name.</param>
    ValueTask RemoveConsumerAsync(string consumerKey);

    /// <summary>
    /// Called during transport restart when a consumer is in <see cref="CircuitBreakerState.HalfOpen"/>.
    /// Invalidates the aborted probe and transitions back to <see cref="CircuitBreakerState.Open"/>,
    /// preserving all failure and escalation history. Does NOT invoke the pause callback — the caller
    /// is responsible for pausing the new transport.
    /// </summary>
    /// <param name="consumerKey">The consumer name.</param>
    ValueTask AbortHalfOpenProbeAsync(string consumerKey);

    /// <summary>
    /// Returns the epoch of the consumer's current Open state. Restart uses it to apply the
    /// replacement transport's pre-pause through the same fence that judges later intents.
    /// </summary>
    bool TryGetOpenEpoch(string consumerKey, out long epoch);
}

internal enum CircuitRetryDecisionKind
{
    Closed,
    Defer,
    ProbeAcquired,
    ProbePending,

    /// <summary>
    /// The circuit is open but no <see cref="ICircuitBreakerStateManager"/> is available to defer the
    /// claim or reserve a probe generation; the claimed lease is retained until it expires.
    /// </summary>
    Retain,
}

internal readonly record struct CircuitRetryDecision(
    CircuitRetryDecisionKind Kind,
    DateTimeOffset? NextProbeAt,
    Task<CircuitRetryProbeOutcome>? ProbeOutcome,
    long Epoch = 0
)
{
    public static CircuitRetryDecision Closed { get; } =
        new(CircuitRetryDecisionKind.Closed, NextProbeAt: null, ProbeOutcome: null);
}

internal enum CircuitRetryProbeOutcomeKind
{
    Closed,
    Reopened,
    Uncertain,
}

[StructLayout(LayoutKind.Auto)]
internal readonly record struct CircuitRetryProbeOutcome(
    CircuitRetryProbeOutcomeKind Kind,
    DateTimeOffset? NextProbeAt
);
