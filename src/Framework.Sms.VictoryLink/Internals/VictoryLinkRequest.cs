// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

namespace Framework.Sms.VictoryLink.Internals;

public sealed class VictoryLinkRequest
{
    [JsonPropertyName("UserName")]
    public required string UserName { get; init; }

    [JsonPropertyName("Password")]
    public required string Password { get; init; }

    [JsonPropertyName("SMSText")]
    public required string SmsText { get; init; }

    [JsonPropertyName("SMSLang")]
    public required string SmsLang { get; init; }

    [JsonPropertyName("SMSSender")]
    public required string SmsSender { get; init; }

    [JsonPropertyName("SMSReceiver")]
    public required string SmsReceiver { get; init; }

    [JsonPropertyName("SMSID")]
    public required string SmsId { get; init; }
}
