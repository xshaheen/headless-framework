// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashIn.Requests;

public sealed record PaymobWalletCashInRequest(
    decimal Amount,
    string WalletPhoneNumber,
    PaymobCashInCustomerData Customer,
    int WalletIntegrationId,
    int ExpirationSeconds = 60 * 60,
    string IframeSrc = "75432"
);
