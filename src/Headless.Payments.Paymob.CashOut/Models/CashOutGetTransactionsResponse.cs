// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashOut.Models;

/// <summary>
/// A paginated response returned by the Paymob CashOut transaction inquiry endpoint.
/// </summary>
[PublicAPI]
public sealed class CashOutGetTransactionsResponse
{
    /// <summary>The total number of transactions matching the query across all pages.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }

    /// <summary>URL of the next page, or <see langword="null"/> when this is the last page.</summary>
    [JsonPropertyName("next")]
    public string? Next { get; init; }

    /// <summary>URL of the previous page, or <see langword="null"/> when this is the first page.</summary>
    [JsonPropertyName("previous")]
    public string? Previous { get; init; }

    /// <summary>The transactions on this page. Returns an empty list when no results are present.</summary>
    [JsonPropertyName("results")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<CashOutTransaction> Results
    {
        get => field ?? [];
        init;
    }
}
