// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Framework.Payments.Paymob.CashIn.Models;

[PublicAPI]
public sealed record PaymobCashInOptions
{
    /// <summary>API base url default: "https://accept.paymobsolutions.com/api"</summary>
    public string ApiBaseUrl { get; set; } = "https://accept.paymobsolutions.com/api";

    /// <summary>Intention url default: "https://accept.paymob.com/v1/intention/"</summary>
    public string CreateIntentionUrl { get; set; } = "https://accept.paymob.com/v1/intention/";

    /// <summary>Refund url default: "https://accept.paymob.com/api/acceptance/void_refund/refund"</summary>
    public string RefundUrl { get; set; } = "https://accept.paymob.com/api/acceptance/void_refund/refund";

    /// <summary>Void refund url default: "https://accept.paymob.com/api/acceptance/void_refund/void"</summary>
    public string VoidRefundUrl { get; set; } = "https://accept.paymob.com/api/acceptance/void_refund/void";

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

    /// <summary>
    /// Token refresh buffer. Auth tokens are refreshed this much before actual expiration.
    /// Default is 55 minutes (5 minutes before Paymob's 60-minute token lifetime).
    /// </summary>
    public TimeSpan TokenRefreshBuffer { get; set; } = TimeSpan.FromMinutes(55);

    /// <summary>New intention API secret key for the merchant.</summary>
    public required string SecretKey { get; set; }
}

public sealed class PaymobCashInOptionsValidator : AbstractValidator<PaymobCashInOptions>
{
    public PaymobCashInOptionsValidator()
    {
        RuleFor(x => x.ApiBaseUrl).NotEmpty().HttpUrl();
        RuleFor(x => x.IframeBaseUrl).NotEmpty().HttpUrl();
        RuleFor(x => x.CreateIntentionUrl).NotEmpty().HttpUrl();
        RuleFor(x => x.RefundUrl).NotEmpty().HttpUrl();
        RuleFor(x => x.VoidRefundUrl).NotEmpty().HttpUrl();
        RuleFor(x => x.ApiKey).NotEmpty();
        RuleFor(x => x.Hmac).NotEmpty();
        RuleFor(x => x.ExpirationPeriod).GreaterThan(60);
        RuleFor(x => x.TokenRefreshBuffer)
            .GreaterThan(TimeSpan.Zero)
            .LessThan(TimeSpan.FromMinutes(60))
            .WithMessage("TokenRefreshBuffer must be positive and less than 60 minutes");
        RuleFor(x => x.SecretKey).NotEmpty();
    }
}
