// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.Payments.Paymob.CashOut.Models;

[PublicAPI]
public sealed record CashOutDisburseRequest
{
    /// <summary>The channel to disburse the e-money through.</summary>
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    /// <summary>Amount to disburse</summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }

    [JsonPropertyName("msisdn")]
    public string? Msisdn { get; init; }

    [JsonPropertyName("bank_card_number")]
    public string? BankCardNumber { get; init; }

    [JsonPropertyName("bank_transaction_type")]
    public string? BankTransactionType { get; init; }

    [JsonPropertyName("bank_code")]
    public string? BankCode { get; init; }

    [JsonPropertyName("full_name")]
    public string? FullName { get; init; }

    /// <summary>aman only.</summary>
    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    /// <summary>aman only.</summary>
    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    /// <summary>aman only.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    public static CashOutDisburseRequest Vodafone(decimal amount, string phoneNumber)
    {
        Argument.IsPositive(amount);
        Argument.IsNotNullOrWhiteSpace(phoneNumber);

        return new CashOutDisburseRequest
        {
            Issuer = "vodafone",
            Amount = amount,
            Msisdn = phoneNumber,
        };
    }

    public static CashOutDisburseRequest Etisalat(decimal amount, string phoneNumber)
    {
        Argument.IsPositive(amount);
        Argument.IsNotNullOrWhiteSpace(phoneNumber);

        return new CashOutDisburseRequest
        {
            Issuer = "etisalat",
            Amount = amount,
            Msisdn = phoneNumber,
        };
    }

    public static CashOutDisburseRequest Orange(decimal amount, string phoneNumber, string fullName)
    {
        Argument.IsPositive(amount);
        Argument.IsNotNullOrWhiteSpace(phoneNumber);
        Argument.IsNotNullOrWhiteSpace(fullName);

        return new CashOutDisburseRequest
        {
            Issuer = "orange",
            Amount = amount,
            Msisdn = phoneNumber,
            FullName = fullName,
        };
    }

    public static CashOutDisburseRequest BankWallet(decimal amount, string phoneNumber, string fullName)
    {
        Argument.IsPositive(amount);
        Argument.IsNotNullOrWhiteSpace(phoneNumber);
        Argument.IsNotNullOrWhiteSpace(fullName);

        return new CashOutDisburseRequest
        {
            Issuer = "bank_wallet",
            Amount = amount,
            Msisdn = phoneNumber,
            FullName = fullName,
        };
    }

    public static CashOutDisburseRequest Accept(
        decimal amount,
        string phoneNumber,
        string firstName,
        string lastName,
        string email
    )
    {
        Argument.IsPositive(amount);
        Argument.IsNotNullOrWhiteSpace(phoneNumber);
        Argument.IsNotNullOrWhiteSpace(firstName);
        Argument.IsNotNullOrWhiteSpace(lastName);
        Argument.IsNotNullOrWhiteSpace(email);

        return new CashOutDisburseRequest
        {
            Issuer = "aman",
            Amount = amount,
            Msisdn = phoneNumber,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
        };
    }

    /// <summary>Create bank card disburse request.</summary>
    /// <param name="amount">Amount to disburse.</param>
    /// <param name="cardNumber">Bank account number, IBAN, or card number.</param>
    /// <param name="bankCode">
    /// One of "AUB", "CITI", "MIDB", "BDC", "HSBC", "CAE", "EGB", "UB", "QNB", "ARAB", "ENBD", "ABK", "NBK",
    /// "ABC", "FAB", "ADIB", "CIB", "HDB", "MISR", "AAIB", "EALB", "EDBE", "FAIB", "BLOM", "ADCB", "BOA", "SAIB",
    /// "NBE", "ABRK", "POST", "NSB", "IDB", "SCB", "MASH", "AIB", "AUDI", "GASC", "ARIB", "PDAC", "NBG", "CBE", "BBE".
    /// </param>
    /// <param name="transactionType"><see cref="BankTransactionTypes"/></param>
    /// <param name="fullName">Account owner full name.</param>
    public static CashOutDisburseRequest BankCard(
        decimal amount,
        string cardNumber,
        string bankCode,
        string transactionType,
        string fullName
    )
    {
        Argument.IsPositive(amount);
        Argument.IsNotNullOrWhiteSpace(cardNumber);
        Argument.IsNotNullOrWhiteSpace(bankCode);
        Argument.IsNotNullOrWhiteSpace(fullName);

        return new CashOutDisburseRequest
        {
            Issuer = "bank_card",
            Amount = amount,
            BankCardNumber = cardNumber,
            BankCode = bankCode,
            BankTransactionType = transactionType,
            FullName = fullName,
        };
    }
}
