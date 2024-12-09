// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashOut.Models;

[PublicAPI]
public sealed class CashOutGetTransactionsRequest
{
    [JsonPropertyName("transactions_ids_list")]
    public IEnumerable<string> TransactionsIds { get; init; } = default!;

    [JsonPropertyName("bank_transactions")]
    public bool? IsBankTransactions { get; init; }
}
