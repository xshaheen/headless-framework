// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

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
    private readonly ConcurrentDictionary<string, ConsumerCircuitBreakerOptions> _options =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Registers or replaces the circuit breaker options for the specified consumer group.
    /// </summary>
    /// <param name="groupName">The consumer group name (must not be null or whitespace).</param>
    /// <param name="options">The circuit breaker overrides to associate with the group.</param>
    internal void Register(string groupName, ConsumerCircuitBreakerOptions options)
    {
        _options[groupName] = options;
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
