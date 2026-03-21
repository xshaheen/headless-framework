// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// A point-in-time snapshot of a circuit breaker's state for programmatic observation.
/// </summary>
public sealed record CircuitBreakerSnapshot
{
    /// <summary>The current circuit breaker state.</summary>
    public required CircuitBreakerState State { get; init; }

    /// <summary>The current escalation level (0 = base).</summary>
    public required int EscalationLevel { get; init; }

    /// <summary>When the circuit entered the Open state, or <see langword="null"/> if not open.</summary>
    public required DateTimeOffset? OpenedAt { get; init; }

    /// <summary>Estimated remaining duration in the Open state, or <see langword="null"/> if not open.</summary>
    public required TimeSpan? EstimatedRemainingOpenDuration { get; init; }
}
