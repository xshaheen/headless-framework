using Framework.Kernel.Checks;

namespace Framework.Kernel.BuildingBlocks.Helpers.System;

[PublicAPI]
public static class CultureHelper
{
    public static bool IsRtl => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    public static IDisposable Use(string culture, string? uiCulture = null)
    {
        Argument.IsNotNull(culture);

        return Use(culture: new CultureInfo(culture), uiCulture: uiCulture is null ? null : new CultureInfo(uiCulture));
    }

    public static IDisposable Use(CultureInfo culture, CultureInfo? uiCulture = null)
    {
        Argument.IsNotNull(culture);
        var currentCulture = CultureInfo.CurrentCulture;
        var currentUiCulture = CultureInfo.CurrentUICulture;

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = uiCulture ?? culture;

        return new DisposeAction(() =>
        {
            CultureInfo.CurrentCulture = currentCulture;
            CultureInfo.CurrentUICulture = currentUiCulture;
        });
    }

    public static bool IsValidCultureCode(string? cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode))
        {
            return false;
        }

        try
        {
            _ = CultureInfo.GetCultureInfo(cultureCode);

            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    public static string GetBaseCultureName(string cultureName)
    {
        return cultureName.Contains('-', StringComparison.Ordinal)
            ? cultureName[..cultureName.IndexOf('-', StringComparison.Ordinal)]
            : cultureName;
    }

    public static void SetCurrentThreadCulture(CultureInfo culture)
    {
        Argument.IsNotNull(culture);

        var currentThread = Thread.CurrentThread;
        currentThread.CurrentCulture = culture;
        currentThread.CurrentUICulture = culture;
    }
}
