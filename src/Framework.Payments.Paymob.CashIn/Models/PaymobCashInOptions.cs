// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Payments.Paymob.CashIn.Models;

[PublicAPI]
public sealed record PaymobCashInOptions
{
    /// <summary>API base url default: "https://accept.paymobsolutions.com/api"</summary>
    public string ApiBaseUrl { get; set; } = "https://accept.paymobsolutions.com/api";

    /// <summary>Iframe base url default: "https://accept.paymob.com/api/acceptance/iframes"</summary>
    public string IframeBaseUrl { get; set; } = "https://accept.paymob.com/api/acceptance/iframes";

    /// <summary>
    /// The unique identifier for the merchant which used to authenticate requests calling
    /// any of the "Paymob Accept"'s API.
    /// </summary>
    public required string ApiKey { get; set; }

    /// <summary>Used to check the integrity of the callback inputs.</summary>
    public required string Hmac { get; set; }

    /// <summary>The default expiration time of this payment token in seconds.</summary>
    public int ExpirationPeriod { get; set; } = 3600;
}

public sealed class PaymobCashInOptionsValidator : AbstractValidator<PaymobCashInOptions>
{
    public PaymobCashInOptionsValidator()
    {
        RuleFor(x => x.ApiBaseUrl).NotEmpty().HttpUrl();
        RuleFor(x => x.IframeBaseUrl).NotEmpty().HttpUrl();
        RuleFor(x => x.ApiKey).NotEmpty();
        RuleFor(x => x.Hmac).NotEmpty();
        RuleFor(x => x.ExpirationPeriod).GreaterThan(60);
    }
}
