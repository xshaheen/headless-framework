// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Context;

/// <summary>
/// Exposes the locale context for the current scope — request, job, or user session.
/// </summary>
/// <remarks>
/// Implementations derive the locale from sources such as the HTTP request's <c>Accept-Language</c>
/// header, the authenticated user's profile, or a fixed default. Used by services that format
/// or localize output.
/// </remarks>
public interface ICurrentLocale
{
    /// <summary>Gets the current locale as a two-letter language code, such as <c>en</c> or <c>ar</c>.</summary>
    string Language { get; }

    /// <summary>
    /// Gets the locale identifier combining language and, when specified, region and formatting
    /// conventions, such as <c>en-US</c> (English, United States) or <c>ar-EG</c> (Arabic, Egypt).
    /// A neutral tag such as <c>en</c> is used when the region is not specified.
    /// </summary>
    string Locale { get; }

    /// <summary>
    /// Gets the <see cref="CultureInfo"/> controlling culture-sensitive operations such as number
    /// and date formatting, sorting, and casing.
    /// </summary>
    CultureInfo LocaleCulture { get; }
}

/// <summary>
/// Immutable locale that always returns <c>en</c> / <c>en-US</c> regardless of the ambient thread culture.
/// </summary>
/// <remarks>
/// Deterministic and thread-safe — safe for background jobs, singleton scope, and tests. Falls back to
/// <see cref="CultureInfo.InvariantCulture"/> under globalization-invariant mode (such as trimmed or
/// container images), where <c>en-US</c> cannot be resolved.
/// </remarks>
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
/// Live locale that reads <see cref="CultureInfo.CurrentCulture"/> on every access, reflecting a culture
/// set by the ASP.NET Core request-localization middleware.
/// </summary>
/// <remarks>
/// Do not use in background jobs without an explicit culture scope, since the ambient culture there
/// is not request-bound.
/// </remarks>
public sealed class CurrentCultureCurrentLocale : ICurrentLocale
{
    /// <inheritdoc/>
    public string Language => LocaleCulture.TwoLetterISOLanguageName;

    /// <inheritdoc/>
    public string Locale => LocaleCulture.Name;

    /// <inheritdoc/>
    public CultureInfo LocaleCulture => CultureInfo.CurrentCulture;
}
