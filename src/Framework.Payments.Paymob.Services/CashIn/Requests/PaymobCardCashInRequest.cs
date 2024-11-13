// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashIn.Requests;

public sealed record PaymobCardCashInRequest(
    decimal Amount,
    PaymobCashInCustomerData Customer,
    int CardIntegrationId,
    int ExpirationSeconds = 60 * 60,
    string IframeSrc = "75432"
);
