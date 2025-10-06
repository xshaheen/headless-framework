// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Intentions;

public sealed class CashInCreateIntentionResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("redirection_url")]
    public string? RedirectionUrl { get; init; }

    [JsonPropertyName("intention_order_id")]
    public int IntentionOrderId { get; init; }

    [JsonPropertyName("client_secret")]
    public required string ClientSecret { get; init; }

    [JsonPropertyName("special_reference")]
    public required string SpecialReference { get; init; }

    [JsonPropertyName("confirmed")]
    public required bool Confirmed { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("created")]
    public DateTime Created { get; init; }

    [JsonPropertyName("card_detail")]
    public object? CardDetail { get; init; }

    [JsonPropertyName("card_tokens")]
    public required List<object> CardTokens { get; init; }

    [JsonPropertyName("object")]
#pragma warning disable CA1720 // Identifier contains type name
    public required string Object { get; init; }
#pragma warning restore CA1720 // Identifier contains type name

    [JsonPropertyName("intention_detail")]
    public required CashInCreateIntentionResponseIntentionDetail IntentionDetail { get; init; }

    [JsonPropertyName("payment_keys")]
    public List<CashInCreateIntentionResponsePaymentKey> PaymentKeys { get; init; } = [];

    [JsonPropertyName("payment_methods")]
    public required List<CashInCreateIntentionResponsePaymentMethod> PaymentMethods { get; init; }

    [JsonPropertyName("extras")]
    public required CashInCreateIntentionResponseExtras Extras { get; init; }
}
