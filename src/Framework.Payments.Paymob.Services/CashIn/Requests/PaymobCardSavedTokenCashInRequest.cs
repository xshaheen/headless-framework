// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashIn.Requests;

public sealed record PaymobCardSavedTokenCashInRequest(
    decimal Amount,
    PaymobCashInCustomerData Customer,
    int SavedTokenIntegrationId,
    string CardToken,
    string? MerchantOrderId = null,
    int ExpirationSeconds = 60 * 60
);
