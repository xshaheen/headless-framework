// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashOut.Requests;

/// <summary>
/// Identifies the mechanism by which funds are credited to a bank account in a CashOut disbursement.
/// Maps to the <c>bank_transaction_type</c> field in the Paymob CashOut API request.
/// </summary>
public enum BankTransactionType
{
    /// <summary>Transfer to bank accounts, debit cards, or similar instruments.</summary>
    CashTransfer = 0,

    /// <summary>Recurring or concurrent payments such as payroll disbursements.</summary>
    Salary = 1,

    /// <summary>Transfer to prepaid cards and Meeza cards.</summary>
    Prepaid = 2,

    /// <summary>Payment to credit cards.</summary>
    CreditCard = 3,
}
