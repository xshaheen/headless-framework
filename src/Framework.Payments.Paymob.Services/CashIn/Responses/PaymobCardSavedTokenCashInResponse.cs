// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashIn.Responses;

public sealed record PaymobCardSavedTokenCashInResponse(bool IsCreated, bool IsSuccess, string OrderId);
