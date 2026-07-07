// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using Headless.Checks;

namespace Headless.Core;

/// <summary>
/// A mutable dictionary of named properties that is pushed onto an <see cref="Microsoft.Extensions.Logging.ILogger" />'s
/// ambient scope via <see cref="Microsoft.Extensions.Logging.ILogger.BeginScope{TState}" />. Each property becomes a
/// structured key/value pair visible to sink implementations (e.g. Serilog, OpenTelemetry).
/// </summary>
[PublicAPI]
public sealed class LogState : IEnumerable<KeyValuePair<string, object?>>
{
    private readonly Dictionary<string, object?> _state = new(StringComparer.Ordinal);

    /// <summary>Gets the number of properties currently held in this log state.</summary>
    public int Count => _state.Count;

    /// <summary>
    /// Gets or sets the value associated with <paramref name="property" />.
    /// Setting a key that already exists overwrites the previous value (last-write-wins).
    /// </summary>
    /// <param name="property">The property name (case-sensitive).</param>
    /// <exception cref="KeyNotFoundException">
    /// Thrown on get when <paramref name="property" /> does not exist in the state.
    /// </exception>
    public object? this[string property]
    {
        get { return _state[property]; }
        set { _state[property] = value; }
    }

    /// <summary>
    /// Adds or updates a property on this log state and returns the same instance for fluent chaining.
    /// If the key already exists its value is overwritten (last-write-wins), so callers such as
    /// <see cref="Microsoft.Extensions.Logging.HeadlessLoggerExtensions.Tag(LogState, string)" /> can safely re-set the same key.
    /// </summary>
    /// <param name="property">The property name. Must not be <see langword="null" />.</param>
    /// <param name="value">The property value. May be <see langword="null" />.</param>
    /// <returns>This <see cref="LogState" /> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="property" /> is <see langword="null" />.
    /// </exception>
    public LogState Property(string property, object? value)
    {
        Argument.IsNotNull(property);
        // Indexer (last-write-wins) rather than Dictionary.Add: Tag()/Critical()/Properties()
        // re-write existing keys, and structured-logging state must never throw on a duplicate.
        _state[property] = value;

        return this;
    }

    /// <summary>
    /// Conditionally adds or updates a property on this log state and returns the same instance
    /// for fluent chaining. When <paramref name="condition" /> is <see langword="false" /> the
    /// state is left unchanged.
    /// </summary>
    /// <param name="property">The property name. Must not be <see langword="null" />.</param>
    /// <param name="value">The property value. May be <see langword="null" />.</param>
    /// <param name="condition">
    /// When <see langword="true" />, the property is set; otherwise this call is a no-op.
    /// </param>
    /// <returns>This <see cref="LogState" /> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="property" /> is <see langword="null" />.
    /// </exception>
    public LogState PropertyIf(string property, object? value, bool condition)
    {
        Argument.IsNotNull(property);

        if (condition)
        {
            _state[property] = value;
        }

        return this;
    }

    /// <summary>
    /// Returns <see langword="true" /> if a property with the given name exists in this log state.
    /// The comparison is case-sensitive (ordinal).
    /// </summary>
    /// <param name="property">The property name to check.</param>
    /// <returns>
    /// <see langword="true" /> if the property exists; otherwise <see langword="false" />.
    /// </returns>
    public bool ContainsProperty(string property)
    {
        return _state.ContainsKey(property);
    }

    /// <summary>Returns an enumerator over the key/value property pairs in this log state.</summary>
    /// <returns>An enumerator of <see cref="KeyValuePair{TKey,TValue}" /> entries.</returns>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _state.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
