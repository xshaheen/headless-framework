// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Persistence;

/// <summary>
/// The correlation and causation ids a store keeps in columns of their own, so a lookup seeks an index instead of
/// reading envelopes.
/// </summary>
internal static class StoredCorrelation
{
    /// <summary>
    /// The longest id a column holds. A longer id is stored as <see langword="null"/>, never cut short: a sized
    /// parameter truncates silently on SQL Server, and a prefix would make the row findable by an id it never carried.
    /// </summary>
    public const int MaxLength = 200;

    /// <summary>Returns the correlation id <paramref name="headers"/> carry, or <see langword="null"/> when it is absent or too long.</summary>
    public static string? CorrelationId(IDictionary<string, string?> headers) => _Of(headers, Headers.CorrelationId);

    /// <summary>Returns the causation id <paramref name="headers"/> carry, or <see langword="null"/> when it is absent or too long.</summary>
    public static string? CausationId(IDictionary<string, string?> headers) => _Of(headers, Headers.CausationId);

    private static string? _Of(IDictionary<string, string?> headers, string name)
    {
        return headers.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) && value.Length <= MaxLength
            ? value
            : null;
    }
}
