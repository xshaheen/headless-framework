// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashIn.Requests;

public sealed record PaymobWalletCashInRequest(
    decimal Amount,
    string WalletPhoneNumber,
    PaymobCashInCustomerData Customer,
    int WalletIntegrationId,
    string? MerchantOrderId = null,
    int ExpirationSeconds = 60 * 60
);
