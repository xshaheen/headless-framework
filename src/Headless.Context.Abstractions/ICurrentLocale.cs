// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Context;

/// <summary>
/// Exposes the locale context for the current execution scope, such as an HTTP request, background job, or user session.
/// </summary>
public interface ICurrentLocale
{
    /// <summary>Gets the current locale as a two-letter language code, such as <c>en</c> or <c>ar</c>.</summary>
    string Language { get; }

    /// <summary>
    /// Gets the locale identifier, including regional conventions when specified, such as <c>en-US</c> or <c>ar-EG</c>.
    /// </summary>
    string Locale { get; }

    /// <summary>Gets the culture info controlling formatting and comparisons.</summary>
    CultureInfo LocaleCulture { get; }
}

/// <summary>
/// Provides an immutable locale that resolves <c>en</c> and <c>en-US</c>.
/// </summary>
/// <remarks>
/// Falls back to <see cref="CultureInfo.InvariantCulture"/> when running under globalization-invariant mode.
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
/// Provides a live locale that reads <see cref="CultureInfo.CurrentCulture"/> on every access.
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
