// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.ReCaptcha.Contracts;

/// <summary>Configuration options for the Google reCAPTCHA siteverify API.</summary>
public sealed class ReCaptchaOptions
{
    /// <summary>
    /// The base URL of the reCAPTCHA verification endpoint. Defaults to <c>https://www.google.com/</c>.
    /// Override only when proxying requests or using a private Google endpoint.
    /// </summary>
    public string VerifyBaseUrl { get; set; } = "https://www.google.com/";

    /// <summary>The public site key issued by Google for this site. Required.</summary>
    public required string SiteKey { get; set; }

    /// <summary>
    /// The secret key issued by Google for this site. Required. Keep this value out of source control
    /// and client-side code.
    /// </summary>
    public required string SiteSecret { get; set; }
}

internal sealed class ReCaptchaOptionsValidator : AbstractValidator<ReCaptchaOptions>
{
    public ReCaptchaOptionsValidator()
    {
        RuleFor(x => x.VerifyBaseUrl).HttpUrl();
        RuleFor(x => x.SiteSecret).NotEmpty();
        RuleFor(x => x.SiteKey).NotEmpty();
    }
}
