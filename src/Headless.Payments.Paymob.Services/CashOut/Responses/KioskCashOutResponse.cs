// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashOut.Responses;

public sealed record KioskCashOutResponse(string TransactionId, CashOutResponseStatus Status, string BillingReference);
