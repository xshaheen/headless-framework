// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Requests;

public sealed record PaymobCardCashInRequest(
    decimal Amount,
    PaymobCashInCustomerData Customer,
    int CardIntegrationId,
    string? MerchantOrderId = null,
    int ExpirationSeconds = 60 * 60,
    string IframeSrc = "75432"
);
