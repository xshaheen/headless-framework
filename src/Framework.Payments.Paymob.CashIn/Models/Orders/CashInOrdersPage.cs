// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Framework.Payments.Paymob.CashIn.Models.Orders;

[PublicAPI]
public sealed class CashInOrdersPage
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("next")]
    public string? Next { get; init; }

    [JsonPropertyName("previous")]
    public string? Previous { get; init; }

    [JsonPropertyName("results")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyCollection<CashInOrder> Results
    {
        get => field ?? [];
        init;
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
