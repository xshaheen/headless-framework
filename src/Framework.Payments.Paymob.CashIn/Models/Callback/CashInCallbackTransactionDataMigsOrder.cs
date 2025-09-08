// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Payments.Paymob.CashIn.Internal;

namespace Framework.Payments.Paymob.CashIn.Models.Callback;

[PublicAPI]
public sealed class CashInCallbackTransactionDataMigsOrder
{
    [JsonPropertyName("acceptPartialAmount")]
    public bool AcceptPartialAmount { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("totalAuthorizedAmount")]
    public decimal TotalAuthorizedAmount { get; init; }

    [JsonPropertyName("totalCapturedAmount")]
    public decimal TotalCapturedAmount { get; init; }

    [JsonPropertyName("totalRefundedAmount")]
    public decimal TotalRefundedAmount { get; init; }

    [JsonPropertyName("creationTime")]
    [JsonConverter(typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter))]
    public DateTimeOffset CreationTime { get; init; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }
}
