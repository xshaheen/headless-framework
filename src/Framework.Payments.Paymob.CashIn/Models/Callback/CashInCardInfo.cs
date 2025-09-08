// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Callback;

public sealed record CashInCardInfo(string CardNumber, string? Type, string? Bank);
