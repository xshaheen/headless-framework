// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn;

public sealed record CashInVoidRefundRequest([property: JsonPropertyName("transaction_id")] string TransactionId);
