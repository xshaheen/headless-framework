// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Messaging.CircuitBreaker;

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
