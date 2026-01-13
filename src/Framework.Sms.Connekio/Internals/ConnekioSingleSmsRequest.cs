// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Sms.Connekio.Internals;

internal sealed class ConnekioSingleSmsRequest
{
    [JsonPropertyName("account_id")]
    public required string AccountId { get; init; }

    [JsonPropertyName("sender")]
    public required string Sender { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("msisdn")]
    public required string Msisdn { get; init; }
}
