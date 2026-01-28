// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Requests;

public sealed record PaymobRefundRequest(string TransactionId, decimal Amount);
