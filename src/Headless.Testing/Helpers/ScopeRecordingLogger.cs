// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Testing.Helpers;

/// <summary>
/// An <see cref="ILogger{TCategoryName}"/> that keeps the scopes opened through it on a real
/// <see cref="LoggerExternalScopeProvider"/>, so a test can assert which scope properties are active on the calling
/// async flow at a given point — for example inside and after a middleware's inner call. Log entries are discarded.
/// </summary>
/// <typeparam name="T">The logger category type.</typeparam>
[PublicAPI]
public sealed class ScopeRecordingLogger<T> : ILogger<T>
{
    private readonly LoggerExternalScopeProvider _scopes = new();

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return _scopes.Push(state);
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) { }

    /// <summary>
    /// Returns the key/value properties of every scope active on the calling async flow, outermost first. Scopes
    /// whose state is not a key/value list contribute nothing.
    /// </summary>
    /// <returns>The active scope properties.</returns>
    public IReadOnlyList<KeyValuePair<string, object?>> GetActiveScopeProperties()
    {
        var properties = new List<KeyValuePair<string, object?>>();

        _scopes.ForEachScope(
            static (scope, list) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    list.AddRange(pairs);
                }
            },
            properties
        );

        return properties;
    }
}
