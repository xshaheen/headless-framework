// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashOut.Requests;

public sealed record BankAccountCashOutRequest(
    decimal Amount,
    string AccountNumber,
    string BankCode,
    BankTransactionType Type,
    string FullName
);
