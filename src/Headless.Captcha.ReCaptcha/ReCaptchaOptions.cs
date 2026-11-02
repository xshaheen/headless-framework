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

    /// <summary>
    /// Gets or sets the lowest reCAPTCHA v3 score, between 0.0 and 1.0, that verification accepts. A valid token whose
    /// score is lower, or that carries no score, fails with <see cref="ReCaptchaError.ScoreTooLow"/>. Defaults to
    /// <c>0.5</c>, Google's suggested starting threshold. Set <c>0</c> to accept every valid token and apply your own
    /// policy to <see cref="ReCaptchaV3VerifyResult.Score"/>. reCAPTCHA v2 ignores it.
    /// </summary>
    public float MinimumScore { get; set; } = 0.5f;

    /// <summary>
    /// Gets or sets the reCAPTCHA v3 action the client token must carry, compared ordinally. A valid token minted for
    /// another action fails with <see cref="ReCaptchaError.ActionMismatch"/>. Defaults to <see langword="null"/>, which
    /// skips the check. reCAPTCHA v2 ignores it.
    /// </summary>
    public string? ExpectedAction { get; set; }

    /// <summary>
    /// Gets or sets the hostname the challenge must have been solved on, compared case-insensitively. A valid token
    /// solved on another host fails with <see cref="ReCaptchaError.HostNameMismatch"/>. Defaults to
    /// <see langword="null"/>, which skips the check. Set it when the site key has domain verification turned off.
    /// </summary>
    public string? ExpectedHostName { get; set; }
}

internal sealed class ReCaptchaOptionsValidator : AbstractValidator<ReCaptchaOptions>
{
    public ReCaptchaOptionsValidator()
    {
        RuleFor(x => x.VerifyBaseUrl).HttpsOrLoopbackHttpUrl();
        RuleFor(x => x.SiteSecret).NotEmpty();
        RuleFor(x => x.SiteKey).NotEmpty();
        RuleFor(x => x.MinimumScore).InclusiveBetween(0f, 1f);
        RuleFor(x => x.ExpectedAction).NotEmpty().When(x => x.ExpectedAction is not null);
        RuleFor(x => x.ExpectedHostName).NotEmpty().When(x => x.ExpectedHostName is not null);
    }
}
