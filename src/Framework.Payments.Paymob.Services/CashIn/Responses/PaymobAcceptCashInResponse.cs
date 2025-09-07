// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashIn.Responses;

public sealed record PaymobKioskCashInResponse(string BillingReference, string OrderId, int Expiration);
