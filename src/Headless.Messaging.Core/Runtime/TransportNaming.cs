// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace Headless.Messaging.Runtime;

/// <summary>
/// Helpers that translate framework message names into broker-native identifiers. Transports use these
/// so wildcard subscriptions and subject/queue names behave consistently across providers.
/// </summary>
internal static partial class TransportNaming
{
    private const int _MaxWildcardLength = 200;
    private const int _MaxWildcardCount = 10;

    /// <summary>
    /// Converts a message-name wildcard pattern into an anchored regular expression. <c>*</c> matches a single
    /// alphanumeric segment and <c>#</c> matches a dotted alphanumeric path; both expand to atomic groups so the
    /// resulting expression cannot backtrack.
    /// </summary>
    /// <param name="wildcard">The message-name pattern to convert.</param>
    /// <returns>An anchored regex pattern, or the escaped literal when the input contains no wildcards.</returns>
    /// <exception cref="ArgumentException">
    /// The pattern exceeds the maximum length (200) or contains too many wildcards (10).
    /// </exception>
    public static string WildcardToRegex(string wildcard)
    {
        if (wildcard.Length > _MaxWildcardLength)
        {
            throw new ArgumentException(
                $"MessageName pattern exceeds maximum length of {_MaxWildcardLength} characters",
                nameof(wildcard)
            );
        }

        var wildcardCount = wildcard.Count(c => c is '*' or '#');
        if (wildcardCount > _MaxWildcardCount)
        {
            throw new ArgumentException(
                $"MessageName pattern contains too many wildcards (max: {_MaxWildcardCount})",
                nameof(wildcard)
            );
        }

        var hasStar = wildcard.Contains('*', StringComparison.Ordinal);
        var hasHash = wildcard.Contains('#', StringComparison.Ordinal);

        if (!hasStar && !hasHash)
        {
            return Regex.Escape(wildcard);
        }

        // Possessive quantifiers (atomic groups) prevent backtracking entirely.
        // Both substitutions are applied sequentially so mixed patterns like "orders.*.#"
        // expand both wildcards instead of leaving the second one as a literal.
        var pattern = "^" + Regex.Escape(wildcard) + "$";

        if (hasStar)
        {
            pattern = pattern.Replace(Regex.Escape("*"), "(?>[0-9a-zA-Z]+)", StringComparison.Ordinal);
        }

        if (hasHash)
        {
            pattern = pattern.Replace(Regex.Escape("#"), "(?>[0-9a-zA-Z\\.]+)", StringComparison.Ordinal);
        }

        return pattern;
    }

    [GeneratedRegex("[\\>\\.\\ \\*]", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, 100)]
    private static partial Regex _NormalizeRegex { get; }

    /// <summary>
    /// Replaces characters that brokers disallow in subject/queue/group names (<c>&gt;</c>, <c>.</c>, space, <c>*</c>)
    /// with underscores. Returns the input unchanged when it contains none of them.
    /// </summary>
    /// <param name="name">The name to normalize.</param>
    public static string Normalize(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        return _NormalizeRegex.IsMatch(name) ? _NormalizeRegex.Replace(name, "_") : name;
    }
}
