// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// Exposes the locale context for the current scope — request, job, or user session. Implementations
/// derive the locale from sources such as the HTTP request's <c>Accept-Language</c> header, the
/// authenticated user's profile, or a fixed default. Used by services that format or localize output.
/// </summary>
public interface ICurrentLocale
{
    /// <summary>Gets the current locale as a neutral language tag (e.g., <c>en</c>, <c>ar</c>).</summary>
    string Language { get; }

    /// <summary>
    /// A combination of language + region + conventions for formatting numbers, dates, currency, etc.
    /// Code examples: "en-US" (English, United States), "en-GB" (English, United Kingdom), "ar-EG" (Arabic, Egypt).
    /// or a neutral language tag like "en" (English), "ar" (Arabic) if region is not specified.
    /// </summary>
    string Locale { get; }

    /// <summary>Controls culture-sensitive operations such as number formatting, date/time formatting, sorting, casing, etc.</summary>
    CultureInfo LocaleCulture { get; }
}
