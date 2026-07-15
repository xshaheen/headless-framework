// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

/// <summary>
/// API request contract for a locale preference expressed as a BCP-47-style language/region pair.
/// Maps to the domain <see cref="PreferredLocale"/> primitive via <see cref="ToPreferredLocale"/>
/// or the implicit conversion.
/// </summary>
/// <param name="Country">ISO 3166-1 alpha-2 country/region code (e.g., <c>"US"</c>, <c>"GB"</c>).</param>
/// <param name="Language">BCP-47 language subtag (e.g., <c>"en"</c>, <c>"ar"</c>).</param>
public sealed record PreferredLocaleRequest(string Country, string Language)
{
    /// <summary>
    /// Returns the locale as a BCP-47-style language tag (e.g., <c>en-US</c>).
    /// </summary>
    public override string ToString()
    {
        return $"{Language}-{Country}";
    }

    /// <summary>Maps this request to the domain <see cref="PreferredLocale"/> primitive.</summary>
    public PreferredLocale ToPreferredLocale()
    {
        return this;
    }

    /// <summary>
    /// Implicitly converts to the domain <see cref="PreferredLocale"/> primitive.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator PreferredLocale?(PreferredLocaleRequest? operand)
    {
        return operand is null ? null : new(operand.Country, operand.Language);
    }
}
