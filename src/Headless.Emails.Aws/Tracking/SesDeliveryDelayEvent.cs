// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>Information about a <c>DeliveryDelay</c> event.</summary>
[PublicAPI]
public sealed record SesDeliveryDelayEvent
{
    /// <summary>The type of delay. One of the <see cref="DelayTypes"/> values.</summary>
    [JsonPropertyName("delayType")]
    public string DelayType { get; init; } = null!;

    /// <summary>The date and time when SES will stop trying to deliver the message.</summary>
    [JsonPropertyName("expirationTime")]
    public DateTimeOffset ExpirationTime { get; init; }

    /// <summary>The date and time when the delay occurred.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The recipients whose delivery was delayed.</summary>
    [JsonPropertyName("delayedRecipients")]
    public DelayedRecipient[] DelayedRecipients { get; init; } = [];

    /// <summary>The IP address of the Message Transfer Agent (MTA) that reported the delay.</summary>
    [JsonPropertyName("reportingMTA")]
    public string? ReportingMta { get; init; }
}
