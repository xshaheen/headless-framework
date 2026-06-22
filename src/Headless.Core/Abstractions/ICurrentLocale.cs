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

/// <summary>
/// Immutable locale that always returns <c>en</c> / <c>en-US</c> regardless of the ambient thread
/// culture. Deterministic and thread-safe — safe for background jobs, singleton scope, and tests.
/// Falls back to <see cref="CultureInfo.InvariantCulture"/> under globalization-invariant mode
/// (e.g. trimmed/container images), where <c>en-US</c> cannot be resolved.
/// </summary>
public sealed class DefaultCurrentLocale : ICurrentLocale
{
    private static readonly CultureInfo _Culture = _ResolveCulture();

    /// <inheritdoc/>
    public string Language => "en";

    /// <inheritdoc/>
    public string Locale => "en-US";

    /// <inheritdoc/>
    public CultureInfo LocaleCulture => _Culture;

    private static CultureInfo _ResolveCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
        catch (CultureNotFoundException)
        {
            // Globalization-invariant mode: only the invariant culture is available.
            return CultureInfo.InvariantCulture;
        }
    }
}

/// <summary>
/// Live locale that reads <see cref="CultureInfo.CurrentCulture"/> on every access, reflecting culture
/// set by ASP.NET Core request-localization middleware. Do not use in background jobs without an explicit
/// culture scope, since the ambient culture there is not request-bound.
/// </summary>
public sealed class CurrentCultureCurrentLocale : ICurrentLocale
{
    /// <inheritdoc/>
    public string Language => LocaleCulture.TwoLetterISOLanguageName;

    /// <inheritdoc/>
    public string Locale => LocaleCulture.Name;

    /// <inheritdoc/>
    public CultureInfo LocaleCulture => CultureInfo.CurrentCulture;
}
