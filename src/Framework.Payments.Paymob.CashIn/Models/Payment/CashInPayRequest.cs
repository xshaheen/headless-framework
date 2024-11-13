// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

namespace Framework.Payments.Paymob.CashIn.Models.Payment;

[PublicAPI]
public sealed class CashInPayRequest
{
    [JsonPropertyName("source")]
    public required CashInSource Source { get; init; }

    [JsonPropertyName("payment_token")]
    public required string PaymentToken { get; init; }
}
