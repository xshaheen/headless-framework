// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

/// <summary>
/// API response view for a locale preference. Maps the domain <see cref="PreferredLocale"/>
/// primitive to a serializable record.
/// </summary>
/// <param name="Country">ISO 3166-1 alpha-2 country/region code (e.g., <c>"US"</c>, <c>"GB"</c>).</param>
/// <param name="Language">BCP-47 language subtag (e.g., <c>"en"</c>, <c>"ar"</c>).</param>
public sealed record PreferredLocaleView(string Country, string Language)
{
    /// <summary>
    /// Maps a domain <see cref="PreferredLocale"/> to a <see cref="PreferredLocaleView"/>.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static PreferredLocaleView? FromPreferredLocale(PreferredLocale? operand) => operand;

    /// <summary>
    /// Implicitly converts a domain <see cref="PreferredLocale"/> to a <see cref="PreferredLocaleView"/>.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator PreferredLocaleView?(PreferredLocale? operand)
    {
        return operand is null ? null : new(operand.Country, operand.Language);
    }
}
