// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// Configuration for a Cloudflare Turnstile provider instance. The <see cref="VerifyBaseUrl"/> is the base address
/// for both the siteverify endpoint and the client API script, so it can be repointed at a test stub.
/// </summary>
[PublicAPI]
public sealed class TurnstileOptions
{
    /// <summary>The base URL of the Cloudflare Turnstile API. Defaults to the public Cloudflare endpoint.</summary>
    public string VerifyBaseUrl { get; set; } = "https://challenges.cloudflare.com/";

    /// <summary>The Turnstile site key rendered into the client widget.</summary>
    public required string SiteKey { get; set; }

    /// <summary>The Turnstile secret key used for server-side verification.</summary>
    public required string SiteSecret { get; set; }
}

internal sealed class TurnstileOptionsValidator : AbstractValidator<TurnstileOptions>
{
    public TurnstileOptionsValidator()
    {
        RuleFor(x => x.VerifyBaseUrl).HttpUrl();
        RuleFor(x => x.SiteKey).NotEmpty();
        RuleFor(x => x.SiteSecret).NotEmpty();
    }
}
