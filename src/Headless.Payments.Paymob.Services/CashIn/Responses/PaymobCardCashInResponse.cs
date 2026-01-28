// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Responses;

public sealed record PaymobCardCashInResponse(string IframeSrc, string PaymentKey, string OrderId, int Expiration);
