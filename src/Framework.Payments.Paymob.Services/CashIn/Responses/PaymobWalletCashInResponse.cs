// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashIn.Responses;

public sealed record PaymobWalletCashInResponse(string RedirectUrl, string OrderId, int Expiration);
