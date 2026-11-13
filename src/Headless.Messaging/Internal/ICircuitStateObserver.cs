// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// Hears every change to a consumer's circuit breaker on this node, after the change was made, so a same-process
/// reader such as the dashboard can show it without polling the breaker.
/// </summary>
/// <remarks>
/// A change is a state transition, a failure count that moved, or a circuit that was removed. An observer runs on the
/// delivery path and on the breaker's timer, so it must return at once and never throw: read
/// <see cref="ICircuitBreakerMonitor.GetSnapshot(string)"/> for what the circuit is now, and do any slow work
/// elsewhere.
/// </remarks>
internal interface ICircuitStateObserver
{
    /// <summary>Called after the circuit <paramref name="circuitKey"/> names changed.</summary>
    /// <param name="circuitKey">The lane-qualified circuit key, as <see cref="ICircuitBreakerMonitor"/> lists it.</param>
    void OnCircuitChanged(string circuitKey);
}
