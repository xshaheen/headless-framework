// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Search;

/// <summary>
/// Builds the Arabic search key of a text in .NET, with the same rules as the database function that builds stored
/// keys. Use it to highlight matches or to search in memory; let the database normalize query terms, so a stored key
/// and its query can never come from two implementations.
/// </summary>
[PublicAPI]
public static class ArabicSearchNormalizer
{
    /// <summary>Returns the search key of <paramref name="value" /> under <paramref name="version" />'s rules.</summary>
    /// <param name="value">The text to normalize.</param>
    /// <param name="version">
    /// The rule version. It has no default: pass the version the stored keys were built with, so moving the keys to a
    /// new version cannot leave a caller on the old one.
    /// </param>
    /// <returns>
    /// The search key: words in the output alphabet separated by single spaces, with no leading or trailing space;
    /// <see cref="string.Empty" /> when the text holds no searchable character; <see langword="null" /> for
    /// <see langword="null" />.
    /// </returns>
    /// <example><c>Normalize("أَحْمَد  عبد-الله", ArabicSearchVersion.V1)</c> returns <c>"احمد عبد الله"</c>.</example>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version" /> is not a defined version.</exception>
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Normalize(string? value, ArabicSearchVersion version)
    {
        var rules = ArabicSearchRuleSet.For(version);

        return value is null ? null : rules.Normalize(value);
    }
}
