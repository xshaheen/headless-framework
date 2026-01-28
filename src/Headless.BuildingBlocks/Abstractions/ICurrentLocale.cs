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

public sealed class DefaultCurrentLocale : ICurrentLocale
{
    public string Language => "en";

    public string Locale => "en-US";

    public CultureInfo LocaleCulture { get; } = CultureInfo.GetCultureInfo("en-US");
}

public sealed class CurrentCultureCurrentLocale : ICurrentLocale
{
    public string Language => LocaleCulture.TwoLetterISOLanguageName;

    public string Locale => LocaleCulture.Name;

    public CultureInfo LocaleCulture => CultureInfo.CurrentCulture;
}
