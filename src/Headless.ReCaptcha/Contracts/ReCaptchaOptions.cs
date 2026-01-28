// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.ReCaptcha.Contracts;

public sealed class ReCaptchaOptions
{
    public string VerifyBaseUrl { get; set; } = "https://www.google.com/";

    public required string SiteKey { get; set; }

    public required string SiteSecret { get; set; }
}

public sealed class ReCaptchaOptionsValidator : AbstractValidator<ReCaptchaOptions>
{
    public ReCaptchaOptionsValidator()
    {
        RuleFor(x => x.VerifyBaseUrl).HttpUrl();
        RuleFor(x => x.SiteSecret).NotEmpty();
        RuleFor(x => x.SiteKey).NotEmpty();
    }
}
