// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Payments.Paymob.CashOut.Models;

/// <summary>
/// Represents a disbursement request to the Paymob CashOut API.
/// </summary>
/// <remarks>
/// Do not construct this record directly. Use the static factory methods that match the intended
/// disbursement channel: <c>Vodafone</c>, <c>Etisalat</c>, <c>Orange</c>, <c>BankWallet</c>,
/// <c>Accept</c> (Aman kiosk), or <c>BankCard</c>. Each factory validates its required fields
/// and sets the correct <c>Issuer</c> value.
/// </remarks>
[PublicAPI]
public sealed record CashOutDisburseRequest
{
    /// <summary>The disbursement channel identifier recognised by Paymob (e.g., <c>vodafone</c>, <c>bank_card</c>, <c>aman</c>).</summary>
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    /// <summary>The amount to disburse in Egyptian Pounds (EGP). Must be positive.</summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }

    /// <summary>The recipient's mobile phone number (MSISDN) for wallet and Orange disbursements.</summary>
    [JsonPropertyName("msisdn")]
    public string? Msisdn { get; init; }

    /// <summary>The recipient's bank account number, IBAN, or card number for bank-card disbursements.</summary>
    [JsonPropertyName("bank_card_number")]
    public string? BankCardNumber { get; init; }

    /// <summary>
    /// The bank transaction type for bank-card disbursements. Use values from <c>BankTransactionTypes</c>.
    /// </summary>
    [JsonPropertyName("bank_transaction_type")]
    public string? BankTransactionType { get; init; }

    /// <summary>The Paymob bank code identifying the recipient's bank for bank-card disbursements.</summary>
    [JsonPropertyName("bank_code")]
    public string? BankCode { get; init; }

    /// <summary>The recipient's full name, required for Orange, bank-wallet, and bank-card disbursements.</summary>
    [JsonPropertyName("full_name")]
    public string? FullName { get; init; }

    /// <summary>The recipient's first name. Required for Aman kiosk (<c>Accept</c>) disbursements.</summary>
    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    /// <summary>The recipient's last name. Required for Aman kiosk (<c>Accept</c>) disbursements.</summary>
    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    /// <summary>The recipient's email address. Required for Aman kiosk (<c>Accept</c>) disbursements.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>Creates a disbursement request targeting the Vodafone Cash mobile wallet.</summary>
    /// <param name="amount">Amount in EGP. Must be positive.</param>
    /// <param name="phoneNumber">Recipient's Vodafone Cash phone number.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="phoneNumber"/> is null or whitespace.</exception>
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

    /// <summary>Creates a disbursement request targeting the Etisalat Cash mobile wallet.</summary>
    /// <param name="amount">Amount in EGP. Must be positive.</param>
    /// <param name="phoneNumber">Recipient's Etisalat Cash phone number.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="phoneNumber"/> is null or whitespace.</exception>
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

    /// <summary>Creates a disbursement request targeting the Orange Money mobile wallet.</summary>
    /// <param name="amount">Amount in EGP. Must be positive.</param>
    /// <param name="phoneNumber">Recipient's Orange Money phone number.</param>
    /// <param name="fullName">Recipient's full name.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="phoneNumber"/> or <paramref name="fullName"/> is null or whitespace.</exception>
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

    /// <summary>Creates a disbursement request targeting a bank-linked mobile wallet.</summary>
    /// <param name="amount">Amount in EGP. Must be positive.</param>
    /// <param name="phoneNumber">Recipient's bank-wallet phone number.</param>
    /// <param name="fullName">Recipient's full name.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="phoneNumber"/> or <paramref name="fullName"/> is null or whitespace.</exception>
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

    /// <summary>
    /// Creates a disbursement request targeting the Aman kiosk network (Paymob Accept channel).
    /// The recipient collects cash at any Aman outlet using the billing reference returned by the API.
    /// </summary>
    /// <param name="amount">Amount in EGP. Must be positive.</param>
    /// <param name="phoneNumber">Recipient's phone number.</param>
    /// <param name="firstName">Recipient's first name.</param>
    /// <param name="lastName">Recipient's last name.</param>
    /// <param name="email">Recipient's email address.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not positive.</exception>
    /// <exception cref="ArgumentException">Any string parameter is null or whitespace.</exception>
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
