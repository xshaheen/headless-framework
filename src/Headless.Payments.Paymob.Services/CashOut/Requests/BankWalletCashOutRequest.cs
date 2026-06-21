// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashOut.Requests;

/// <summary>Parameters for a CashOut disbursement to a bank-linked mobile wallet.</summary>
/// <param name="Amount">The amount to disburse in Egyptian Pounds (EGP). Must be positive.</param>
/// <param name="PhoneNumber">The recipient's bank-wallet phone number.</param>
/// <param name="FullName">The recipient's full name.</param>
public sealed record BankWalletCashOutRequest(decimal Amount, string PhoneNumber, string FullName);
