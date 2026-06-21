// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Sms.Vodafone;

/// <summary>Options for the Vodafone Egypt SMS provider.</summary>
/// <remarks>
/// The Vodafone Egypt API uses an XML request body. Each call is authenticated with <see cref="AccountId"/>,
/// <see cref="Password"/>, and a per-request HMAC-SHA256 signature derived from <see cref="SecureHash"/>.
/// The signature covers the account id, password, sender name, recipient MSISDN, and SMS text.
/// </remarks>
public sealed class VodafoneSmsOptions
{
    /// <summary>The Vodafone Egypt SMS submission endpoint. Defaults to the Vodafone Egypt production URL.</summary>
    public string SendSmsEndpoint { get; init; } = "https://e3len.vodafone.com.eg/web2sms/sms/submit/";

    /// <summary>The registered sender name included in the XML request body and the HMAC signature input.</summary>
    public required string Sender { get; init; }

    /// <summary>The Vodafone Egypt account identifier used for authentication and HMAC signature computation.</summary>
    public required string AccountId { get; init; }

    /// <summary>The Vodafone Egypt account password used for authentication and HMAC signature computation.</summary>
    public required string Password { get; init; }

    /// <summary>
    /// The HMAC-SHA256 secret key (as a plain string) used to sign each request. This value is provided
    /// by Vodafone Egypt during account provisioning.
    /// </summary>
    public required string SecureHash { get; init; }
}

[UsedImplicitly]
internal sealed class VodafoneSmsOptionsValidator : AbstractValidator<VodafoneSmsOptions>
{
    public VodafoneSmsOptionsValidator()
    {
        RuleFor(x => x.SendSmsEndpoint).NotEmpty().HttpsOnlyUrl();
        RuleFor(x => x.Sender).NotEmpty();
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.SecureHash).NotEmpty();
    }
}
