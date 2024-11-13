// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashOut.Responses;

public sealed record KioskCashOutResponse(string TransactionId, CashOutResponseStatus Status, string BillingReference);
