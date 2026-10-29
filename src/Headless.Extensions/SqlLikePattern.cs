// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless;

/// <summary>
/// Builds SQL <c>LIKE</c> patterns from user text so the text matches literally instead of as wildcards. Use the
/// pattern with <see cref="EscapeCharacter"/> as the <c>ESCAPE</c> clause (<c>LIKE @p ESCAPE '\'</c>) or as the
/// <c>escapeCharacter</c> argument of EF Core's <c>EF.Functions.Like</c>.
/// </summary>
/// <remarks>
/// The escaped set is the escape character itself, <c>%</c>, <c>_</c>, and SQL Server's <c>[</c>. Escaping
/// <c>[</c> is harmless on PostgreSQL and SQLite, where an escaped ordinary character matches itself, so one
/// pattern works on every supported provider.
/// </remarks>
[PublicAPI]
public static class SqlLikePattern
{
    /// <summary>The escape character the patterns use, for the <c>ESCAPE</c> clause.</summary>
    public const string EscapeCharacter = "\\";

    /// <summary>Escapes <paramref name="value"/> so it matches itself exactly under <see cref="EscapeCharacter"/>.</summary>
    /// <param name="value">The literal text to match.</param>
    /// <returns>The escaped text, with no wildcard added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string Escape(string value)
    {
        Argument.IsNotNull(value);

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
    }

    /// <summary>Returns a pattern that matches text starting with <paramref name="value"/>.</summary>
    /// <param name="value">The literal prefix to match.</param>
    /// <returns>The escaped prefix followed by <c>%</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string StartsWith(string value)
    {
        return Escape(value) + "%";
    }

    /// <summary>Returns a pattern that matches text containing <paramref name="value"/>.</summary>
    /// <param name="value">The literal text to find.</param>
    /// <returns>The escaped text wrapped in <c>%</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string Contains(string value)
    {
        return "%" + Escape(value) + "%";
    }
}
