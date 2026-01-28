// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Refunds;

public sealed record CashInRefundRequest(
    [property: JsonPropertyName("transaction_id")] string TransactionId,
    [property: JsonPropertyName("amount_cents")] string AmountCents
);
