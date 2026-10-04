// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using FluentValidation;

namespace Headless.Messaging;

/// <summary>
/// Stores per-consumer circuit breaker overrides set through
/// <c>Tune(identity, consumer =&gt; consumer.CircuitBreaker(...))</c> or configuration.
/// </summary>
/// <remarks>
/// This registry is an internal singleton. Both the consumer builder (at startup) and the
/// <see cref="ICircuitBreakerStateManager"/> (at runtime) reference the same instance so
/// per-consumer overrides are always visible without modifying <c>ConsumerMetadata</c>.
/// </remarks>
internal sealed class ConsumerCircuitBreakerRegistry
{
    private static readonly ConsumerCircuitBreakerOptionsValidator _Validator = new();
    private readonly ConcurrentDictionary<string, ConsumerCircuitBreakerOptions> _options = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers circuit breaker options for the specified consumer.
    /// </summary>
    /// <param name="consumerKey">The consumer name (must not be null or whitespace).</param>
    /// <param name="options">The circuit breaker overrides to associate with the consumer.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a circuit breaker override for <paramref name="consumerKey"/> is already registered.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="options"/> contains invalid values.
    /// </exception>
    internal void Register(string consumerKey, ConsumerCircuitBreakerOptions options)
    {
        _Validator.ValidateAndThrow(options);

        if (!_options.TryAdd(consumerKey, options))
        {
            throw new InvalidOperationException(
                $"Circuit breaker already registered for consumer '{consumerKey}'. "
                    + "Each consumer can only have one circuit breaker override."
            );
        }
    }

    /// <summary>
    /// Attempts to retrieve circuit breaker overrides for the specified consumer.
    /// </summary>
    /// <param name="consumerKey">The consumer name.</param>
    /// <param name="options">
    /// The registered options, or <see langword="null"/> if no overrides are configured.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if overrides exist for the consumer; otherwise <see langword="false"/>.
    /// </returns>
    internal bool TryGet(string consumerKey, out ConsumerCircuitBreakerOptions? options)
    {
        return _options.TryGetValue(consumerKey, out options);
    }
}
