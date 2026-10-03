// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>
/// The email's commonly used headers, as reported in <see cref="MailDetails.CommonHeaders"/>. SES sends
/// this as a JSON object (not a list of name/value pairs); address headers are arrays.
/// </summary>
[PublicAPI]
public sealed record EmailCommonHeaders
{
    /// <summary>The <c>From</c> addresses.</summary>
    [JsonPropertyName("from")]
    public string[]? From { get; init; }

    /// <summary>The <c>To</c> addresses.</summary>
    [JsonPropertyName("to")]
    public string[]? To { get; init; }

    /// <summary>The <c>Cc</c> addresses.</summary>
    [JsonPropertyName("cc")]
    public string[]? Cc { get; init; }

    /// <summary>The <c>Bcc</c> addresses.</summary>
    [JsonPropertyName("bcc")]
    public string[]? Bcc { get; init; }

    /// <summary>The <c>Sender</c> addresses.</summary>
    [JsonPropertyName("sender")]
    public string[]? Sender { get; init; }

    /// <summary>The <c>Reply-To</c> addresses.</summary>
    [JsonPropertyName("replyTo")]
    public string[]? ReplyTo { get; init; }

    /// <summary>The <c>Return-Path</c> address.</summary>
    [JsonPropertyName("returnPath")]
    public string? ReturnPath { get; init; }

    /// <summary>The original <c>Message-ID</c> header from the message you passed to SES.</summary>
    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }

    /// <summary>The <c>Date</c> header.</summary>
    [JsonPropertyName("date")]
    public string? Date { get; init; }

    /// <summary>The <c>Subject</c> header.</summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    /// <summary>Captures any common headers SES reports that are not modeled above.</summary>
    [JsonExtensionData]
    public IDictionary<string, object?> ExtensionData { get; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}
