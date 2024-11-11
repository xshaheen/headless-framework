// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Payments.Paymob.Services.CashOut.Responses;

public sealed record KioskCashOutResponse(string TransactionId, CashOutResponseStatus Status, string BillingReference);
