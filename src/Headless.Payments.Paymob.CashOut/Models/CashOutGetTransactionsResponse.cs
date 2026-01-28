// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Headless.Payments.Paymob.CashOut.Models;

[PublicAPI]
public sealed class CashOutGetTransactionsResponse
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("next")]
    public string? Next { get; init; }

    [JsonPropertyName("previous")]
    public string? Previous { get; init; }

    [JsonPropertyName("results")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<CashOutTransaction> Results
    {
        get => field ?? [];
        init;
    }
}
