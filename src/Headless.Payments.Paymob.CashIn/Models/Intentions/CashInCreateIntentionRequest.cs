// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Intentions;

/// <summary>Request payload for the Paymob Intention API (<c>IPaymobCashInBroker.CreateIntentionAsync</c>).</summary>
[PublicAPI]
public sealed class CashInCreateIntentionRequest
{
    /// <summary>The total amount in the smallest currency unit (integer cents), e.g. <c>10000</c> for 100.00 EGP.</summary>
    [JsonPropertyName("amount")]
    public required long Amount { get; init; }

    /// <summary>
    /// Specify the currency for the specific region in this parameter. It should be similar to
    /// the currency of the integration ID being used. The currency supporting in UAE is “AED” and “USD”.
    /// </summary>
    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    /// <summary>
    /// Pass a unique special reference number in this parameter. Refer to a unique or special identifier or
    /// reference associated with a transaction or order. It can be used for tracking or categorizing specific
    /// types of transactions, and it returns within the transaction callback under <see cref="CashInCreateIntentionResponseCreationExtras.MerchantOrderId"/>
    /// </summary>
    [JsonPropertyName("special_reference")]
    public string SpecialReference { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// This refers to the URL that will receive the transaction-processed callback POST request
    /// after the transaction succeeds or fails, with all transaction details in the body of
    /// the request and this only works with card integration ID.
    /// </summary>
    [JsonPropertyName("notification_url")]
    public string? NotificationUrl { get; init; }

    /// <summary>
    /// This refers to the transaction response callback URL that the customer will be redirected to after
    /// the transaction succeeds or fails, with most transaction details included as query parameters
    /// and this only works with card integration ID.
    /// </summary>
    [JsonPropertyName("redirection_url")]
    public string? RedirectionUrl { get; init; }

    /// <summary>
    /// Provide the configured Integration ID here. Merchants can use the ID as an integer or the name
    /// </summary>
    [JsonPropertyName("payment_methods")]
    public required IReadOnlyList<long> PaymentMethods { get; init; } = [];

    /// <summary>The customer's billing data (name, contact details, and address) required by Paymob.</summary>
    [JsonPropertyName("billing_data")]
    public required CashInCreateIntentionRequestBillingData BillingData { get; init; }

    /// <summary>The line items included in this intention. May be empty when itemisation is not required.</summary>
    [JsonPropertyName("items")]
    public required IReadOnlyList<CashInCreateIntentionRequestItem> Items { get; init; } = [];

    /// <summary>Optional free-form extra data echoed back in the transaction callback.</summary>
    [JsonPropertyName("extras")]
    public IReadOnlyDictionary<string, object>? Extras { get; init; }
}
