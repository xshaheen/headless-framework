// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Intentions;

public class CashInCreateIntentionResponsePaymentKey
{
    [JsonPropertyName("integration")]
    public required int Integration { get; init; }

    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("gateway_type")]
    public required string GatewayType { get; init; }

    [JsonPropertyName("iframe_id")]
    public string? IframeId { get; init; }

    [JsonPropertyName("order_id")]
    public required int OrderId { get; init; }

    [JsonPropertyName("redirection_url")]
    public required string RedirectionUrl { get; init; }
}
