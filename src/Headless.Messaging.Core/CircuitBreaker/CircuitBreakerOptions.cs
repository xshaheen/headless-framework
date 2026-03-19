// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Configuration options for the circuit breaker state manager.
/// </summary>
/// <remarks>
/// These options control the sensitivity and recovery behavior of the circuit breaker.
/// Additional options (such as integration with <c>MessagingOptions</c>) will be wired in a subsequent step.
/// </remarks>
public sealed class CircuitBreakerOptions
{
    /// <summary>
    /// Gets or sets the number of consecutive transient failures required to open the circuit.
    /// Default is 5.
    /// </summary>
    public int FailureThreshold { get; init; } = 5;

    /// <summary>
    /// Gets or sets the initial duration the circuit stays open before transitioning to half-open.
    /// Subsequent trips will escalate this duration exponentially up to <see cref="MaxOpenDuration"/>.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan OpenDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the maximum duration the circuit can stay open, regardless of escalation level.
    /// Default is 240 seconds (4 minutes).
    /// </summary>
    public TimeSpan MaxOpenDuration { get; init; } = TimeSpan.FromSeconds(240);

    /// <summary>
    /// Gets or sets the number of successful close cycles required to reset the escalation level back to zero.
    /// Default is 3.
    /// </summary>
    public int SuccessfulCyclesToResetEscalation { get; init; } = 3;
}
