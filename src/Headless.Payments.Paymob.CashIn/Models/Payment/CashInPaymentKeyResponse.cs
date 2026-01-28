// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Payment;

[PublicAPI]
public sealed class CashInPaymentKeyResponse
{
    [JsonPropertyName("token")]
    public required string PaymentKey { get; init; }
}
