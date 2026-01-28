// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Callback;

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
