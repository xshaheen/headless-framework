// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>Information about a <c>Delivery</c> event.</summary>
[PublicAPI]
public sealed record SesDeliveryEvent
{
    /// <summary>The date and time when SES delivered the email to the recipient's mail server.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The time in milliseconds between when SES accepted the request from the sender and when SES passed
    /// the message to the recipient's mail server.
    /// </summary>
    [JsonPropertyName("processingTimeMillis")]
    public int ProcessingTimeMillis { get; init; }

    /// <summary>The list of intended recipients that the delivery event applies to.</summary>
    [JsonPropertyName("recipients")]
    public string[] Recipients { get; init; } = [];

    /// <summary>
    /// The SMTP response message of the remote ISP that accepted the email from SES. This message varies
    /// by email, by receiving mail server, and by receiving ISP.
    /// </summary>
    [JsonPropertyName("smtpResponse")]
    public string? SmtpResponse { get; init; }

    /// <summary>The host name of the SES mail server that sent the mail.</summary>
    [JsonPropertyName("reportingMTA")]
    public string? ReportingMta { get; init; }

    /// <summary>The IP address of the MTA to which SES delivered the email.</summary>
    [JsonPropertyName("remoteMtaIp")]
    public string? RemoteMtaIp { get; init; }
}
