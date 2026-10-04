// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Captcha;

/// <summary>Configures settings for a Google reCAPTCHA provider instance.</summary>
[PublicAPI]
public sealed class ReCaptchaOptions
{
    /// <summary>
    /// Gets or sets the base URL of the reCAPTCHA API. External endpoints require HTTPS; HTTP is accepted only for
    /// loopback development and test servers. Defaults to the public Google endpoint.
    /// </summary>
    public string VerifyBaseUrl { get; set; } = "https://www.google.com/";

    /// <summary>Gets or sets the reCAPTCHA site key rendered into the client widget or script.</summary>
    public required string SiteKey { get; set; }

    /// <summary>Gets or sets the reCAPTCHA secret key used for server-side verification.</summary>
    public required string SiteSecret { get; set; }
}

internal sealed class ReCaptchaOptionsValidator : AbstractValidator<ReCaptchaOptions>
{
    public ReCaptchaOptionsValidator()
    {
        RuleFor(x => x.VerifyBaseUrl).HttpsOrLoopbackHttpUrl();
        RuleFor(x => x.SiteSecret).NotEmpty();
        RuleFor(x => x.SiteKey).NotEmpty();
    }
}
