// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Configuration options for the circuit breaker state manager.
/// </summary>
/// <remarks>
/// These options control the sensitivity and recovery behavior of the circuit breaker.
/// They apply globally to all consumer groups unless overridden per consumer via
/// <c>ConsumerCircuitBreakerOptions</c>.
/// </remarks>
public sealed class CircuitBreakerOptions
{
    /// <summary>
    /// Gets or sets the number of consecutive transient failures required to open the circuit.
    /// Must be greater than zero. Default is 5.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Gets or sets the initial duration the circuit stays open before transitioning to half-open.
    /// Subsequent trips will escalate this duration exponentially up to <see cref="MaxOpenDuration"/>.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan OpenDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the maximum duration the circuit can stay open, regardless of escalation level.
    /// Must be greater than or equal to <see cref="OpenDuration"/>.
    /// Default is 240 seconds (4 minutes).
    /// </summary>
    public TimeSpan MaxOpenDuration { get; set; } = TimeSpan.FromSeconds(240);

    /// <summary>
    /// Gets or sets the number of successful close cycles required to reset the escalation level back to zero.
    /// Default is 3.
    /// </summary>
    public int SuccessfulCyclesToResetEscalation { get; set; } = 3;

    /// <summary>
    /// Gets or sets a predicate that determines whether an exception is transient and should
    /// count toward the circuit breaker failure threshold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transient exceptions indicate infrastructure or connectivity problems (broker, network, etc.)
    /// and are appropriate signals to open the circuit. Non-transient exceptions such as
    /// deserialization errors or validation failures indicate problems with the message itself and
    /// should not trip the circuit breaker.
    /// </para>
    /// <para>
    /// Defaults to <see cref="CircuitBreakerDefaults.IsTransient"/>.
    /// </para>
    /// </remarks>
    public Func<Exception, bool> IsTransientException { get; set; } = CircuitBreakerDefaults.IsTransient;
}
