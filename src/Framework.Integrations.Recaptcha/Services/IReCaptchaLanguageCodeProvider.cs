// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Integrations.Recaptcha.Services;

public interface IReCaptchaLanguageCodeProvider
{
    string GetLanguageCode();
}

public sealed class CultureInfoReCaptchaLanguageCodeProvider : IReCaptchaLanguageCodeProvider
{
    public string GetLanguageCode() => CultureInfo.CurrentUICulture.ToString();
}
