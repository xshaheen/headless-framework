// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

public interface ICurrentLocale
{
    /// <summary>Gets the current locale as a neutral language tag (e.g., `en`, `ar`).</summary>
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

/// <summary>
/// Immutable locale that always returns <c>en</c> / <c>en-US</c> regardless of the ambient thread
/// culture. Deterministic and thread-safe — safe for background jobs, singleton scope, and tests.
/// </summary>
public sealed class DefaultCurrentLocale : ICurrentLocale
{
    public string Language => "en";

    public string Locale => "en-US";

    public CultureInfo LocaleCulture { get; } = CultureInfo.GetCultureInfo("en-US");
}

/// <summary>
/// Live locale that reads <see cref="CultureInfo.CurrentCulture"/> on every access, reflecting culture
/// set by ASP.NET Core request-localization middleware. Do not use in background jobs without an explicit
/// culture scope, since the ambient culture there is not request-bound.
/// </summary>
public sealed class CurrentCultureCurrentLocale : ICurrentLocale
{
    public string Language => LocaleCulture.TwoLetterISOLanguageName;

    public string Locale => LocaleCulture.Name;

    public CultureInfo LocaleCulture => CultureInfo.CurrentCulture;
}
