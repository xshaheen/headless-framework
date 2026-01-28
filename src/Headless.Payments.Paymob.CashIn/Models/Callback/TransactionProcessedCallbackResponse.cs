// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Internals;

namespace Headless.Payments.Paymob.CashIn.Models.Callback;

[PublicAPI]
public sealed class TransactionProcessedCallbackResponse
{
    [JsonPropertyName("response_received_at")]
    [JsonConverter(typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter))]
    public DateTimeOffset ResponseReceivedAt { get; init; }

    [JsonPropertyName("callback_url")]
    public required string CallbackUrl { get; init; }

    [JsonPropertyName("response")]
    public required TransactionProcessedCallbackResponseObj Response { get; init; }
}
