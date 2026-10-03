// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

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
