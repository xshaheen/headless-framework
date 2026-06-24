// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>Information about the original email that produced an SES sending event.</summary>
[PublicAPI]
public sealed record MailDetails
{
    /// <summary>
    /// A unique ID that Amazon SES assigned to the message and returned when the message was sent.
    /// </summary>
    /// <remarks>
    /// Any message ID inside <see cref="Headers"/> or <see cref="CommonHeaders"/> is from the original
    /// message you passed to SES; this field is the ID SES subsequently assigned.
    /// </remarks>
    [JsonPropertyName("messageId")]
    public string MessageId { get; init; } = null!;

    /// <summary>The date and time when the message was sent.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The email address the message was sent from (the envelope MAIL FROM address).</summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = null!;

    /// <summary>
    /// The ARN of the identity used to send the email. For sending authorization this is the ARN of the
    /// identity that the identity owner authorized the delegate sender to use.
    /// </summary>
    [JsonPropertyName("sourceArn")]
    public string? SourceArn { get; init; }

    /// <summary>
    /// The AWS account ID used to send the email. For sending authorization this is the delegate
    /// sender's account ID.
    /// </summary>
    [JsonPropertyName("sendingAccountId")]
    public string SendingAccountId { get; init; } = null!;

    /// <summary>The list of email addresses that were recipients of the original mail.</summary>
    [JsonPropertyName("destination")]
    public string[] Destination { get; init; } = [];

    /// <summary>
    /// Whether the headers are truncated in the notification, which occurs when the headers are larger
    /// than 10 KB.
    /// </summary>
    [JsonPropertyName("headersTruncated")]
    public bool HeadersTruncated { get; init; }

    /// <summary>
    /// The email's original headers. Each entry has a <see cref="EmailHeader.Name"/> and a
    /// <see cref="EmailHeader.Value"/>.
    /// </summary>
    [JsonPropertyName("headers")]
    public EmailHeader[]? Headers { get; init; }

    /// <summary>The email's original, commonly used headers.</summary>
    [JsonPropertyName("commonHeaders")]
    public EmailCommonHeaders? CommonHeaders { get; init; }

    /// <summary>
    /// The tags associated with the email. Each key maps to a list of values, for example
    /// <c>ses:configuration-set</c>, <c>ses:source-ip</c>, or <c>ses:from-domain</c>.
    /// </summary>
    [JsonPropertyName("tags")]
    public IReadOnlyDictionary<string, string[]>? Tags { get; init; }

    /// <summary>Captures any fields SES adds to the mail object that are not modeled above.</summary>
    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}

/// <summary>A single email header, as reported in <see cref="MailDetails.Headers"/>.</summary>
/// <param name="Name">The header name.</param>
/// <param name="Value">The header value.</param>
[PublicAPI]
public sealed record EmailHeader(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value
);

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
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
