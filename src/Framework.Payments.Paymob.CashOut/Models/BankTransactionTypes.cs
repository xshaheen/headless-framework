// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashOut.Models;

public static class BankTransactionTypes
{
    /// <summary>For bank accounts, debit cards etc.</summary>
    public const string CashTransfer = "cash_transfer";

    /// <summary>For credit cards payments.</summary>
    public const string CreditCard = "credit_card";

    /// <summary>For prepaid cards and Meeza cards payments.</summary>
    public const string PrepaidCard = "prepaid_card";

    /// <summary>For concurrent or repeated payments.</summary>
    public const string Salary = "salary";
}
