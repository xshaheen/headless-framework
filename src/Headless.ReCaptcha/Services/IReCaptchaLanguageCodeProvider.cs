// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.ReCaptcha.Services;

public interface IReCaptchaLanguageCodeProvider
{
    string GetLanguageCode();
}

public sealed class CultureInfoReCaptchaLanguageCodeProvider : IReCaptchaLanguageCodeProvider
{
    public string GetLanguageCode() => CultureInfo.CurrentUICulture.ToString();
}
