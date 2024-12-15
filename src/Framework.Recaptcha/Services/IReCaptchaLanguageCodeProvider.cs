// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Recaptcha.Services;

public interface IReCaptchaLanguageCodeProvider
{
    string GetLanguageCode();
}

public sealed class CultureInfoReCaptchaLanguageCodeProvider : IReCaptchaLanguageCodeProvider
{
    public string GetLanguageCode() => CultureInfo.CurrentUICulture.ToString();
}
