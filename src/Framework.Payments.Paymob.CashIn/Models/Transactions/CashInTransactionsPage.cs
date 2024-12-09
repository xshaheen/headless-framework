// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Transactions;

[PublicAPI]
public sealed class CashInTransactionsPage
{
    private readonly IReadOnlyCollection<CashInTransaction>? _results;

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("next")]
    public string? Next { get; init; }

    [JsonPropertyName("previous")]
    public string? Previous { get; init; }

    [JsonPropertyName("results")]
    public IReadOnlyCollection<CashInTransaction> Results
    {
        get => _results ?? Array.Empty<CashInTransaction>();
        init => _results = value;
    }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }

    public bool HasPrevious()
    {
        return Previous is not null;
    }

    public bool HasNext()
    {
        return Next is not null;
    }
}
