// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

namespace Framework.Payments.Paymob.CashIn.Models.Payment;

[PublicAPI]
public sealed class CashInPaymentKeyResponse
{
    [JsonPropertyName("token")]
    public required string PaymentKey { get; init; }
}
