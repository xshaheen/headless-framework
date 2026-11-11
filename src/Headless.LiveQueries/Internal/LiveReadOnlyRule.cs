// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries.Internal;

/// <summary>The error a live query gets when it writes, or announces a write, while it runs.</summary>
internal static class LiveReadOnlyRule
{
    private const string _Rule =
        "A live query must be read-only: it runs "
        + "again after every change to what it read, for every subscriber, so a write inside it would repeat "
        + "without bound. Move the write into a command the client sends once.";

    /// <summary>Builds the exception for a live query that tried to write <paramref name="keys" />.</summary>
    /// <param name="operation">What the query called, as the application wrote it.</param>
    /// <param name="keys">The dependency keys of the tables the write names; empty when it names none.</param>
    public static InvalidOperationException Violated(string operation, IReadOnlyCollection<string> keys)
    {
        var target = keys.Count == 0 ? "the database" : string.Join(", ", keys.Order(StringComparer.Ordinal));

        return new InvalidOperationException($"A live query called {operation} and would write to {target}. {_Rule}");
    }

    /// <summary>Builds the exception for a live query that tried to announce a write to <paramref name="keys" />.</summary>
    /// <param name="keys">The dependency keys the query passed to <c>InvalidateAsync</c>.</param>
    public static InvalidOperationException Announced(IReadOnlyCollection<string> keys)
    {
        var target = string.Join(", ", keys.Order(StringComparer.Ordinal));

        return new InvalidOperationException(
            $"A live query called InvalidateAsync and would announce a write to {target}. {_Rule}"
        );
    }
}
