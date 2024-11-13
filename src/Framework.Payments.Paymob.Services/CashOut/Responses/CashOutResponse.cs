// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashOut.Responses;

public sealed record CashOutResponse(string TransactionId, CashOutResponseStatus Status);
