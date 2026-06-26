// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace Headless.Primitives;

/// <summary>Represents a user's preferred locale as a country/language pair (for example <c>en-USA</c>).</summary>
/// <param name="country">Three-letter ISO country code in uppercase.</param>
/// <param name="language">Two-letter ISO language code in lowercase.</param>
[PublicAPI]
[ComplexType]
[DebuggerDisplay("{" + nameof(Language) + "}-{" + nameof(Country) + "}")]
public sealed class PreferredLocale(string country, string language) : IEquatable<PreferredLocale>
{
    private PreferredLocale()
        : this(null!, null!) { }

    /// <summary>
    /// Three-letter ISO country code, normalized to uppercase (invariant) on construction so the documented
    /// casing invariant always holds and equality is effectively case-insensitive on the country code.
    /// </summary>
    public string Country { get; private init; } = country?.ToUpperInvariant()!;

    /// <summary>
    /// Two-letter ISO language code, normalized to lowercase (invariant) on construction so the documented
    /// casing invariant always holds and equality is effectively case-insensitive on the language code.
    /// </summary>
    public string Language { get; private init; } = language?.ToLowerInvariant()!;

    /// <summary>Returns the locale formatted as <c>{Language}-{Country}</c>.</summary>
    public override string ToString() => $"{Language}-{Country}";

    /// <summary>Determines whether this locale equals <paramref name="other"/> by country and language.</summary>
    /// <param name="other">The locale to compare with.</param>
    /// <returns><see langword="true"/> if both locales are equal; otherwise, <see langword="false"/>.</returns>
    public bool Equals(PreferredLocale? other)
    {
        if (other is null)
        {
            return false;
        }

        return ReferenceEquals(this, other)
            || (
                string.Equals(Country, other.Country, StringComparison.Ordinal)
                && string.Equals(Language, other.Language, StringComparison.Ordinal)
            );
    }

    /// <summary>Determines whether <paramref name="obj"/> is a <see cref="PreferredLocale"/> equal to this instance.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is an equal locale; otherwise, <see langword="false"/>.</returns>
    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || (obj is PreferredLocale other && Equals(other));
    }

    /// <summary>Returns a hash code derived from the country and language.</summary>
    /// <returns>A hash code for the current locale.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Country, Language);
    }

    /// <summary>Determines whether two locales are equal.</summary>
    /// <param name="left">The first locale to compare.</param>
    /// <param name="right">The second locale to compare.</param>
    /// <returns><see langword="true"/> if the locales are equal (including both being <see langword="null"/>); otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(PreferredLocale? left, PreferredLocale? right)
    {
        return left?.Equals(right) ?? right is null;
    }

    /// <summary>Determines whether two locales are different.</summary>
    /// <param name="left">The first locale to compare.</param>
    /// <param name="right">The second locale to compare.</param>
    /// <returns><see langword="true"/> if the locales differ; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(PreferredLocale? left, PreferredLocale? right)
    {
        return !(left == right);
    }
}
