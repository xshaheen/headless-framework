// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashOut.Models;

/// <summary>
/// The request body for the Paymob CashOut transaction inquiry endpoint.
/// </summary>
/// <remarks>
/// Serialised as JSON and sent in the body of the GET request to the transaction inquiry URL.
/// </remarks>
[PublicAPI]
public sealed class CashOutGetTransactionsRequest
{
    /// <summary>The list of Paymob transaction IDs to look up.</summary>
    [JsonPropertyName("transactions_ids_list")]
    public required IEnumerable<string> TransactionsIds { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the IDs refer to bank-card or bank-wallet transactions.
    /// When <see langword="false"/> or <see langword="null"/>, the IDs refer to mobile-wallet transactions.
    /// </summary>
    [JsonPropertyName("bank_transactions")]
    public bool? IsBankTransactions { get; init; }
}
