// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Core;

/// <summary>Helpers for inspecting and temporarily overriding the current thread's culture.</summary>
[PublicAPI]
public static class CultureHelper
{
    /// <summary>Gets a value indicating whether the current UI culture is a right-to-left language.</summary>
    public static bool IsRtl => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    /// <summary>
    /// Temporarily sets the current culture (and optionally the current UI culture) for the calling flow,
    /// restoring the previous cultures when the returned scope is disposed.
    /// </summary>
    /// <param name="culture">The culture name (for example <c>"en-US"</c>) to apply as the current culture.</param>
    /// <param name="uiCulture">
    /// The culture name to apply as the current UI culture; when <see langword="null"/>, <paramref name="culture"/> is used for both.
    /// </param>
    /// <returns>An <see cref="IDisposable"/> that restores the previously effective cultures when disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is <see langword="null"/>.</exception>
    /// <exception cref="CultureNotFoundException">Thrown when <paramref name="culture"/> or <paramref name="uiCulture"/> does not name a supported culture.</exception>
    [MustDisposeResource]
    public static IDisposable Use(string culture, string? uiCulture = null)
    {
        Argument.IsNotNull(culture);

        return Use(
            culture: CultureInfo.GetCultureInfo(culture),
            uiCulture: uiCulture is null ? null : CultureInfo.GetCultureInfo(uiCulture)
        );
    }

    /// <summary>
    /// Temporarily sets the current culture (and optionally the current UI culture) for the calling flow,
    /// restoring the previous cultures when the returned scope is disposed.
    /// </summary>
    /// <param name="culture">The <see cref="CultureInfo"/> to apply as the current culture.</param>
    /// <param name="uiCulture">
    /// The <see cref="CultureInfo"/> to apply as the current UI culture; when <see langword="null"/>, <paramref name="culture"/> is used for both.
    /// </param>
    /// <returns>An <see cref="IDisposable"/> that restores the previously effective cultures when disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is <see langword="null"/>.</exception>
    [MustDisposeResource]
    public static IDisposable Use(CultureInfo culture, CultureInfo? uiCulture = null)
    {
        Argument.IsNotNull(culture);
        var currentCulture = CultureInfo.CurrentCulture;
        var currentUiCulture = CultureInfo.CurrentUICulture;

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = uiCulture ?? culture;

        return DisposableFactory.Create(() =>
        {
            CultureInfo.CurrentCulture = currentCulture;
            CultureInfo.CurrentUICulture = currentUiCulture;
        });
    }

    /// <summary>Determines whether <paramref name="cultureCode"/> names a culture supported by the runtime.</summary>
    /// <param name="cultureCode">The culture code to validate (for example <c>"en-US"</c>).</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="cultureCode"/> is non-blank and resolves to a known culture; otherwise <see langword="false"/>.
    /// </returns>
    public static bool IsValidCultureCode(string? cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode))
        {
            return false;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureCode);

            // On ICU runtimes GetCultureInfo does not throw for well-formed-but-unknown tags (e.g. "xx-XX");
            // it synthesizes a placeholder culture flagged UserCustomCulture. Real cultures (including genuine
            // pseudo-locales such as "qps-ploc") never carry that flag, so treat synthesized ones as invalid.
            return !culture.CultureTypes.HasFlag(CultureTypes.UserCustomCulture);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts the neutral (language-only) portion of a culture name by stripping the region suffix,
    /// for example returning <c>"en"</c> from <c>"en-US"</c>.
    /// </summary>
    /// <param name="cultureName">The full culture name (e.g. <c>"en-US"</c>) to reduce to its base language code.</param>
    /// <returns>
    /// The substring before the first <c>'-'</c>, or <paramref name="cultureName"/> unchanged when it contains no <c>'-'</c>.
    /// </returns>
    public static string GetBaseCultureName(string cultureName)
    {
        return cultureName.Contains('-', StringComparison.Ordinal)
            ? cultureName[..cultureName.IndexOf('-', StringComparison.Ordinal)]
            : cultureName;
    }

    /// <summary>Sets both the current culture and current UI culture of the calling thread to <paramref name="culture"/>.</summary>
    /// <param name="culture">The <see cref="CultureInfo"/> to assign to the current thread.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is <see langword="null"/>.</exception>
    public static void SetCurrentThreadCulture(CultureInfo culture)
    {
        Argument.IsNotNull(culture);

        var currentThread = Thread.CurrentThread;
        currentThread.CurrentCulture = culture;
        currentThread.CurrentUICulture = culture;
    }
}
