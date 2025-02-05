// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Intentions;

public sealed class CashInCreateIntentionResponsePaymentMethod
{
    [JsonPropertyName("integration_id")]
    public required int IntegrationId { get; init; }

    [JsonPropertyName("alias")]
    public object? Alias { get; init; }

    [JsonPropertyName("name")]
    public object? Name { get; init; }

    [JsonPropertyName("method_type")]
    public required string MethodType { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("live")]
    public required bool Live { get; init; }

    [JsonPropertyName("use_cvc_with_moto")]
    public required bool UseCvcWithMoto { get; init; }
}
