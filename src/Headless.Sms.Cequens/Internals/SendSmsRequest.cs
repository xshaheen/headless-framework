// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms.Cequens.Internals;

public sealed class SendSmsRequest
{
    [JsonPropertyName("senderName")]
    public required string SenderName { get; init; }

    [JsonPropertyName("messageText")]
    public required string MessageText { get; init; }

    [JsonPropertyName("recipients")]
    public required string Recipients { get; init; }

    [JsonPropertyName("messageType")]
    public string MessageType { get; init; } = "text";

    /// <summary>Set it to <see langword="true"/>, if the messageText contains URL and you want to shorten it.</summary>
    [JsonPropertyName("shortURL")]
    public bool ShortUrl { get; init; } = false;

    /// <summary>An integer used as an identifier for your request to track it.</summary>
    [JsonPropertyName("clientMessageId")]
    public int? ClientMessageId { get; init; }
}
