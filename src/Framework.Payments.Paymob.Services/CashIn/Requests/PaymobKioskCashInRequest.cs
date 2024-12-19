// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashIn.Requests;

public sealed record PaymobKioskCashInRequest(
    decimal Amount,
    PaymobCashInCustomerData Customer,
    int KioskIntegrationId,
    int ExpirationSeconds = 60 * 60
);
