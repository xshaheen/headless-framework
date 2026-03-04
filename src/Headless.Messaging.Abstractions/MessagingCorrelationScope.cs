// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>
/// Provides an ambient correlation context for messaging operations using AsyncLocal storage.
/// This scope enables correlation ID and sequence tracking across asynchronous message publishing flows.
/// Supports nesting by preserving parent scopes and restoring them on disposal.
/// </summary>
/// <remarks>
/// Use <see cref="Begin"/> to create a new correlation scope. The scope will automatically
/// restore the previous scope when disposed. Thread-safe sequence increments are provided
/// via <see cref="IncrementSequence"/>.
/// </remarks>
public sealed class MessagingCorrelationScope : IDisposable
{
    private static readonly AsyncLocal<MessagingCorrelationScope?> _Current = new();
    private readonly MessagingCorrelationScope? _parent;
    private int _sequence;

    /// <summary>
    /// Gets the current ambient correlation scope, or <c>null</c> if no scope is active.
    /// </summary>
    public static MessagingCorrelationScope? Current => _Current.Value;

    /// <summary>
    /// Gets the correlation identifier for this scope.
    /// </summary>
    public string CorrelationId { get; }

    private MessagingCorrelationScope(string correlationId, int initialSequence)
    {
        CorrelationId = correlationId;
        _sequence = initialSequence;
        _parent = _Current.Value;
        _Current.Value = this;
    }

    /// <summary>
    /// Begins a new correlation scope with the specified correlation ID and optional initial sequence.
    /// </summary>
    /// <param name="correlationId">The correlation identifier for this scope. Cannot be null or empty.</param>
    /// <param name="initialSequence">The initial sequence number. Defaults to 0.</param>
    /// <returns>A new <see cref="MessagingCorrelationScope"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="correlationId"/> is null or empty.</exception>
    /// <remarks>
    /// The returned scope should be disposed to restore the previous ambient scope.
    /// Nesting is supported - each scope preserves its parent and restores it on disposal.
    /// </remarks>
    public static MessagingCorrelationScope Begin(string correlationId, int initialSequence = 0)
    {
        Argument.IsNotNullOrEmpty(correlationId);

        return new MessagingCorrelationScope(correlationId, initialSequence);
    }

    /// <summary>
    /// Increments the correlation sequence in a thread-safe manner and returns the new value.
    /// </summary>
    /// <returns>The incremented sequence number.</returns>
    public int IncrementSequence() => Interlocked.Increment(ref _sequence);

    /// <summary>
    /// Restores the previous ambient correlation scope.
    /// </summary>
    public void Dispose()
    {
        _Current.Value = _parent;
    }
}
