// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Captcha;

/// <summary>
/// Configuration for a Cloudflare Turnstile provider instance. The <see cref="VerifyBaseUrl"/> is the base address
/// for both the siteverify endpoint and the client API script, so it can be repointed at a test stub.
/// </summary>
[PublicAPI]
public sealed class TurnstileOptions
{
    /// <summary>
    /// The base URL of the Cloudflare Turnstile API. External endpoints require HTTPS; HTTP is accepted only for
    /// loopback development and test servers. Defaults to the public Cloudflare endpoint.
    /// </summary>
    public string VerifyBaseUrl { get; set; } = "https://challenges.cloudflare.com/";

    /// <summary>The Turnstile site key rendered into the client widget.</summary>
    public required string SiteKey { get; set; }

    /// <summary>The Turnstile secret key used for server-side verification.</summary>
    public required string SiteSecret { get; set; }

    /// <summary>
    /// The widget action a token must carry. A token solved for another action fails with the error code
    /// <c>action-mismatch</c>. Defaults to <see langword="null"/>, which skips the check.
    /// </summary>
    public string? ExpectedAction { get; set; }

    /// <summary>
    /// The host name a token must have been solved on, compared ignoring case. A token solved on another host fails with
    /// the error code <c>hostname-mismatch</c>. Defaults to <see langword="null"/>, which skips the check.
    /// </summary>
    public string? ExpectedHostName { get; set; }
}

internal sealed class TurnstileOptionsValidator : AbstractValidator<TurnstileOptions>
{
    public TurnstileOptionsValidator()
    {
        RuleFor(x => x.VerifyBaseUrl).HttpsOrLoopbackHttpUrl();
        RuleFor(x => x.SiteKey).NotEmpty();
        RuleFor(x => x.SiteSecret).NotEmpty();
    }
}
