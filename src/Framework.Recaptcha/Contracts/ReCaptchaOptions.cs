// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Recaptcha.Contracts;

public sealed class ReCaptchaOptions
{
    public string VerifyBaseUrl { get; set; } = "https://www.google.com/";

    public required string SiteKey { get; set; }

    public required string SiteSecret { get; set; }
}

public sealed class RecaptchaOptionsValidator : AbstractValidator<ReCaptchaOptions>
{
    public RecaptchaOptionsValidator()
    {
        RuleFor(x => x.VerifyBaseUrl).HttpUrl();
        RuleFor(x => x.SiteSecret).NotEmpty();
        RuleFor(x => x.SiteKey).NotEmpty();
    }
}
