// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Payments.Paymob.CashIn.Models;

/// <summary>
/// Configuration options for the Paymob Accept (CashIn) integration.
/// </summary>
/// <remarks>
/// Register via <c>SetupPaymobCashIn.AddPaymobCashIn</c>. Options are validated on startup using
/// FluentValidation; missing required properties or invalid URLs cause the application to fail fast.
/// </remarks>
[PublicAPI]
public sealed record PaymobCashInOptions
{
    /// <summary>
    /// Base URL for the legacy Paymob Accept API.
    /// Defaults to <c>https://accept.paymobsolutions.com/api</c>.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://accept.paymobsolutions.com/api";

    /// <summary>
    /// Endpoint URL for the Intention API (newer unified checkout flow).
    /// Defaults to <c>https://accept.paymob.com/v1/intention/</c>.
    /// </summary>
    public string CreateIntentionUrl { get; set; } = "https://accept.paymob.com/v1/intention/";

    /// <summary>
    /// Endpoint URL used to submit refund requests.
    /// Defaults to <c>https://accept.paymob.com/api/acceptance/void_refund/refund</c>.
    /// </summary>
    public string RefundUrl { get; set; } = "https://accept.paymob.com/api/acceptance/void_refund/refund";

    /// <summary>
    /// Endpoint URL used to submit void requests.
    /// Defaults to <c>https://accept.paymob.com/api/acceptance/void_refund/void</c>.
    /// </summary>
    public string VoidRefundUrl { get; set; } = "https://accept.paymob.com/api/acceptance/void_refund/void";

    /// <summary>
    /// Base URL used to construct the hosted card-payment iframe embed URL.
    /// Defaults to <c>https://accept.paymob.com/api/acceptance/iframes</c>.
    /// </summary>
    public string IframeBaseUrl { get; set; } = "https://accept.paymob.com/api/acceptance/iframes";

    /// <summary>
    /// The merchant API key used to authenticate requests against the legacy Paymob Accept API.
    /// Obtain this from the Paymob dashboard under Settings &gt; Account Info.
    /// </summary>
    public required string ApiKey { get; set; }

    /// <summary>
    /// The HMAC secret key used to verify the integrity of callback submissions from Paymob.
    /// Every transaction and token callback is signed with this key; use <c>IPaymobCashInBroker.Validate</c>
    /// to verify incoming webhooks.
    /// </summary>
    public required string Hmac { get; set; }

    /// <summary>
    /// Default expiration period for payment keys, in seconds. Must be greater than 60.
    /// Defaults to <c>3600</c> (60 minutes).
    /// </summary>
    public int ExpirationPeriod { get; set; } = 3600;

    /// <summary>
    /// Controls how early the cached authentication token is refreshed before it expires.
    /// Must be positive and less than 60 minutes. Defaults to 55 minutes, which renews the token
    /// 5 minutes before Paymob's 60-minute token lifetime ends.
    /// </summary>
    public TimeSpan TokenRefreshBuffer { get; set; } = TimeSpan.FromMinutes(55);

    /// <summary>
    /// The merchant secret key used to authenticate requests against the Intention API.
    /// This is separate from <c>ApiKey</c> and is issued alongside the intention-flow integration.
    /// </summary>
    public required string SecretKey { get; set; }

    // Redact the secrets so the compiler-generated ToString() never leaks them into logs or diagnostics.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ApiBaseUrl = ").Append(ApiBaseUrl);
        builder.Append(", CreateIntentionUrl = ").Append(CreateIntentionUrl);
        builder.Append(", RefundUrl = ").Append(RefundUrl);
        builder.Append(", VoidRefundUrl = ").Append(VoidRefundUrl);
        builder.Append(", IframeBaseUrl = ").Append(IframeBaseUrl);
        builder.Append(", ApiKey = ***");
        builder.Append(", Hmac = ***");
        builder.Append(", ExpirationPeriod = ").Append(ExpirationPeriod);
        builder.Append(", TokenRefreshBuffer = ").Append(TokenRefreshBuffer);
        builder.Append(", SecretKey = ***");

        return true;
    }
}

internal sealed class PaymobCashInOptionsValidator : AbstractValidator<PaymobCashInOptions>
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
