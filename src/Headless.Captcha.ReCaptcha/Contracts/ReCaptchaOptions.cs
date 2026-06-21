// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>Configuration for a Google reCAPTCHA provider instance (shared by v2 and v3 registrations).</summary>
[PublicAPI]
public sealed class ReCaptchaOptions
{
    /// <summary>The base URL of the reCAPTCHA API. Defaults to the public Google endpoint.</summary>
    public string VerifyBaseUrl { get; set; } = "https://www.google.com/";

    /// <summary>The reCAPTCHA site key rendered into the client widget/script.</summary>
    public required string SiteKey { get; set; }

    /// <summary>The reCAPTCHA secret key used for server-side verification.</summary>
    public required string SiteSecret { get; set; }
}

[PublicAPI]
public sealed class ReCaptchaOptionsValidator : AbstractValidator<ReCaptchaOptions>
{
    public ReCaptchaOptionsValidator()
    {
        RuleFor(x => x.VerifyBaseUrl).HttpUrl();
        RuleFor(x => x.SiteSecret).NotEmpty();
        RuleFor(x => x.SiteKey).NotEmpty();
    }
}
