// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Headless.Captcha;

/// <summary>Applies reCAPTCHA v2 widget attributes to tag helper output.</summary>
internal static class ReCaptchaV2Widget
{
    public static void ApplyAttributes(
        TagHelperOutput output,
        string siteKey,
        string? badge,
        string? theme,
        string? size,
        string? tabIndex,
        string? callback,
        string? expiredCallback,
        string? errorCallback
    )
    {
        output.Attributes.Add("class", "g-recaptcha");
        output.Attributes.Add("data-sitekey", siteKey);
        _AddIf(output, "data-badge", badge);
        _AddIf(output, "data-theme", theme);
        _AddIf(output, "data-size", size);
        _AddIf(output, "data-tabindex", tabIndex);
        _AddIf(output, "data-callback", callback);
        _AddIf(output, "data-expired-callback", expiredCallback);
        _AddIf(output, "data-error-callback", errorCallback);
    }

    private static void _AddIf(TagHelperOutput output, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            output.Attributes.Add(name, value);
        }
    }
}
