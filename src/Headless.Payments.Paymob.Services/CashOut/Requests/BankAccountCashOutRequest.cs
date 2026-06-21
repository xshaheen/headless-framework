// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashOut.Requests;

/// <summary>Parameters for a CashOut disbursement directly to a bank account or card.</summary>
/// <param name="Amount">The amount to disburse in Egyptian Pounds (EGP). Must be positive.</param>
/// <param name="AccountNumber">The recipient's bank account number, IBAN, or card number.</param>
/// <param name="BankCode">
/// The Paymob bank code identifying the recipient's bank. Use values from <c>CashOutBanks.All</c>
/// or refer to the bank codes listed in the <c>CashOutDisburseRequest.BankCard</c> documentation.
/// </param>
/// <param name="Type">The bank transaction type determining how the funds are credited to the recipient.</param>
/// <param name="FullName">The recipient's full name as registered with the bank.</param>
public sealed record BankAccountCashOutRequest(
    decimal Amount,
    string AccountNumber,
    string BankCode,
    BankTransactionType Type,
    string FullName
);
