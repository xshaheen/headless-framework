// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using System.Text.Json.Serialization;

namespace Framework.Payments.Paymob.CashIn.Models.Callback;

[PublicAPI]
public sealed class TransactionProcessedCallbackResponseObj
{
    [JsonPropertyName("encoding")]
    public string? Encoding { get; init; }

    [JsonPropertyName("headers")]
    public string? Headers { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}
