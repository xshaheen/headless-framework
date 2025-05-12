// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Orders;

[PublicAPI]
public sealed class CashInOrdersPage
{
    private readonly IReadOnlyCollection<CashInOrder>? _results;

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("next")]
    public string? Next { get; init; }

    [JsonPropertyName("previous")]
    public string? Previous { get; init; }

    [JsonPropertyName("results")]
    public IReadOnlyCollection<CashInOrder> Results
    {
        get => _results ?? [];
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
