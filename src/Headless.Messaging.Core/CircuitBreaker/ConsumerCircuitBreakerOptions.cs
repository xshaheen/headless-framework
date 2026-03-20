// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Per-consumer overrides for circuit breaker behavior. Any property left <see langword="null"/>
/// falls back to the global <see cref="CircuitBreakerOptions"/> configured in
/// <c>MessagingOptions.CircuitBreaker</c>.
/// </summary>
public sealed class ConsumerCircuitBreakerOptions
{
    /// <summary>
    /// Gets a value indicating whether the circuit breaker is active for this consumer.
    /// When <see langword="false"/>, the circuit breaker is bypassed entirely.
    /// Default is <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the number of consecutive transient failures required to open the circuit
    /// for this consumer. When <see langword="null"/>, the global
    /// <see cref="CircuitBreakerOptions.FailureThreshold"/> is used.
    /// </summary>
    public int? FailureThreshold { get; init; }

    /// <summary>
    /// Gets the initial duration the circuit stays open before transitioning to half-open
    /// for this consumer. When <see langword="null"/>, the global
    /// <see cref="CircuitBreakerOptions.OpenDuration"/> is used.
    /// </summary>
    public TimeSpan? OpenDuration { get; init; }

    /// <summary>
    /// Gets a predicate that determines whether an exception is transient for this consumer.
    /// When <see langword="null"/>, the global <see cref="CircuitBreakerOptions.IsTransientException"/>
    /// predicate is used.
    /// </summary>
    public Func<Exception, bool>? IsTransientException { get; init; }
}
