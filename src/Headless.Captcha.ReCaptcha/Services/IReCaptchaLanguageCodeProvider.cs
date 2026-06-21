// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>Supplies the language code appended to the reCAPTCHA script URL (<c>?hl=</c>).</summary>
[PublicAPI]
public interface IReCaptchaLanguageCodeProvider
{
    string GetLanguageCode();
}

/// <summary>Default <see cref="IReCaptchaLanguageCodeProvider"/> that uses the current UI culture.</summary>
[PublicAPI]
public sealed class CultureInfoReCaptchaLanguageCodeProvider : IReCaptchaLanguageCodeProvider
{
    public string GetLanguageCode() => CultureInfo.CurrentUICulture.ToString();
}
