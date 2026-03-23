// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using FluentValidation;

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Stores per-consumer-group circuit breaker overrides registered via
/// <c>IConsumerBuilder&lt;T&gt;.WithCircuitBreaker()</c>.
/// </summary>
/// <remarks>
/// This registry is an internal singleton. Both the consumer builder (at startup) and the
/// <see cref="ICircuitBreakerStateManager"/> (at runtime) reference the same instance so
/// per-group overrides are always visible without modifying <c>ConsumerMetadata</c>.
/// </remarks>
internal sealed class ConsumerCircuitBreakerRegistry
{
    private static readonly ConsumerCircuitBreakerOptionsValidator _Validator = new();
    private readonly ConcurrentDictionary<string, ConsumerCircuitBreakerOptions> _options = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers circuit breaker options for the specified consumer group.
    /// </summary>
    /// <param name="groupName">The consumer group name (must not be null or whitespace).</param>
    /// <param name="options">The circuit breaker overrides to associate with the group.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a circuit breaker override for <paramref name="groupName"/> is already registered.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="options"/> contains invalid values.
    /// </exception>
    internal void Register(string groupName, ConsumerCircuitBreakerOptions options)
    {
        _Validator.ValidateAndThrow(options);

        if (!_options.TryAdd(groupName, options))
        {
            throw new InvalidOperationException(
                $"Circuit breaker already registered for group '{groupName}'. "
                    + "Each consumer group can only have one circuit breaker override. "
                    + "Check that you haven't configured the same group via both "
                    + "Subscribe<T>().WithCircuitBreaker() and AddConsumer<T,M>().WithCircuitBreaker()."
            );
        }
    }

    /// <summary>
    /// Registers or updates circuit breaker options for the specified consumer group.
    /// Used internally by builders that defer registration until the final group name is known.
    /// </summary>
    internal void RegisterOrUpdate(string groupName, ConsumerCircuitBreakerOptions options)
    {
        _Validator.ValidateAndThrow(options);
        _options[groupName] = options;
    }

    /// <summary>
    /// Removes a previously registered override for the specified consumer group, if any.
    /// </summary>
    internal void Remove(string groupName)
    {
        _options.TryRemove(groupName, out _);
    }

    /// <summary>
    /// Attempts to retrieve circuit breaker overrides for the specified consumer group.
    /// </summary>
    /// <param name="groupName">The consumer group name.</param>
    /// <param name="options">
    /// The registered options, or <see langword="null"/> if no overrides are configured.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if overrides exist for the group; otherwise <see langword="false"/>.
    /// </returns>
    internal bool TryGet(string groupName, out ConsumerCircuitBreakerOptions? options)
    {
        return _options.TryGetValue(groupName, out options);
    }
}
