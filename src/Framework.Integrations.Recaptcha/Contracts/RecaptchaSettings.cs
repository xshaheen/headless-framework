// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Integrations.Recaptcha.Contracts;

public sealed class ReCaptchaSettings
{
    public string VerifyBaseUrl { get; set; } = "https://www.google.com/";

    public required string SiteKey { get; set; }

    public required string SiteSecret { get; set; }
}

public sealed class RecaptchaSettingsValidator : AbstractValidator<ReCaptchaSettings>
{
    public RecaptchaSettingsValidator()
    {
        RuleFor(x => x.VerifyBaseUrl).HttpUrl();
        RuleFor(x => x.SiteSecret).NotEmpty();
        RuleFor(x => x.SiteKey).NotEmpty();
    }
}
