// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Intentions;

public sealed class CashInCreateIntentionRequest
{
    [JsonPropertyName("amount")]
    public required decimal Amount { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("special_reference")]
    public string SpecialReference { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("notification_url")]
    public string? NotificationUrl { get; init; }

    [JsonPropertyName("redirection_url")]
    public string? RedirectionUrl { get; init; }

    [JsonPropertyName("special_reference")]
    public required int ExpirationSeconds { get; init; }

    [JsonPropertyName("payment_methods")]
    public required List<int> PaymentMethods { get; init; } = [];

    [JsonPropertyName("billing_data")]
    public required CashInCreateIntentionRequestBillingData BillingData { get; init; }

    [JsonPropertyName("items")]
    public required List<CashInCreateIntentionRequestItem> Items { get; init; } = [];

    [JsonPropertyName("extras")]
    public Dictionary<string, object>? Extras { get; init; }
}
