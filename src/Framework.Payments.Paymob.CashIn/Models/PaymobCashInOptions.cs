// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using FluentValidation;
using Framework.FluentValidation;

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

    /// <summary>New intention API secret key for the merchant.</summary>
    public required string SecretKey { get; set; }

    /// <summary>Serialization options for JSON.</summary>
    public JsonSerializerOptions SerializationOptions { get; set; } =
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

    /// <summary>Deserialization options for JSON.</summary>
    public JsonSerializerOptions DeserializationOptions { get; set; } =
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            AllowTrailingCommas = true,
        };
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
        RuleFor(x => x.SecretKey).NotEmpty();
    }
}
