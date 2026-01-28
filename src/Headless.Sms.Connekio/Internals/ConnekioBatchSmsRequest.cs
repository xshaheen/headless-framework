// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms.Connekio.Internals;

internal sealed class ConnekioBatchSmsRequest
{
    [JsonPropertyName("account_id")]
    public required string AccountId { get; init; }

    [JsonPropertyName("sender")]
    public required string Sender { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("mobile_list")]
    public required List<ConnekioRecipient> MobileList { get; init; }
}

internal sealed class ConnekioRecipient
{
    [JsonPropertyName("msisdn")]
    public required string Msisdn { get; init; }
}
