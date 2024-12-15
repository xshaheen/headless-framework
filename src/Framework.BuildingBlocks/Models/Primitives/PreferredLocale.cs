// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Primitives;

[PublicAPI]
[ComplexType]
[DebuggerDisplay("{" + nameof(Language) + "}-{" + nameof(Country) + "}")]
public sealed class PreferredLocale(string country, string language) : IEquatable<PreferredLocale>
{
    public static readonly PreferredLocale ArEg = new("EG", "ar");
    public static readonly PreferredLocale EnUs = new("US", "en");

    private PreferredLocale()
        : this(null!, null!) { }

    /// <summary>Three-letter ISO country code in uppercase.</summary>
    public string Country { get; private init; } = country;

    /// <summary>Two-letter ISO language code in lowercase.</summary>
    public string Language { get; private init; } = language;

    public override string ToString() => $"{Language}-{Country}";

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

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || (obj is PreferredLocale other && Equals(other));
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Country, Language);
    }

    public static bool operator ==(PreferredLocale? left, PreferredLocale? right)
    {
        return left?.Equals(right) ?? right is null;
    }

    public static bool operator !=(PreferredLocale? left, PreferredLocale? right)
    {
        return !(left == right);
    }
}
