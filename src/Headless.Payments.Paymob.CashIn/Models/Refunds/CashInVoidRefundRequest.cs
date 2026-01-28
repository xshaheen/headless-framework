// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Refunds;

public sealed record CashInVoidRefundRequest([property: JsonPropertyName("transaction_id")] string TransactionId);
