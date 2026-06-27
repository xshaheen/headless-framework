// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;

namespace Tests;

/// <summary>The default reCAPTCHA language-code provider derives the code from the current UI culture.</summary>
public sealed class ReCaptchaLanguageProviderTests
{
    [Fact]
    public void default_provider_returns_current_ui_culture()
    {
        var original = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            new CultureInfoReCaptchaLanguageCodeProvider().GetLanguageCode().Should().Be("fr-FR");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
