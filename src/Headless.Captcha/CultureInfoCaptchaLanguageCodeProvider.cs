// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// Resolves the captcha language code from <see cref="CultureInfo.CurrentUICulture"/>.
/// </summary>
[PublicAPI]
public sealed class CultureInfoCaptchaLanguageCodeProvider : ICaptchaLanguageCodeProvider
{
    /// <inheritdoc />
    public string GetLanguageCode()
    {
        return CultureInfo.CurrentUICulture.ToString();
    }
}
