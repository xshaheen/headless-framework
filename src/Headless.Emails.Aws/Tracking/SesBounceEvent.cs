// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>
/// Information about a <c>Bounce</c> event. SES publishes hard bounces and soft bounces that it no longer
/// retries. A <see cref="BounceType"/> of <see cref="BounceTypes.Permanent"/> means you should remove the
/// recipient from your mailing list; <see cref="BounceTypes.Transient"/> may succeed on a later send.
/// </summary>
[PublicAPI]
public sealed record SesBounceEvent
{
    /// <summary>The type of bounce, as determined by SES. One of the <see cref="BounceTypes"/> values.</summary>
    [JsonPropertyName("bounceType")]
    public string BounceType { get; init; } = null!;

    /// <summary>The subtype of the bounce, as determined by SES. One of the <see cref="BounceSubTypes"/> values.</summary>
    [JsonPropertyName("bounceSubType")]
    public string BounceSubType { get; init; } = null!;

    /// <summary>The date and time when the ISP sent the bounce notification.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>A unique ID for the bounce.</summary>
    [JsonPropertyName("feedbackId")]
    public string FeedbackId { get; init; } = null!;

    /// <summary>
    /// The value of the <c>Reporting-MTA</c> field from the DSN — the MTA that attempted the delivery,
    /// relay, or gateway operation described in the DSN. Present only when a DSN was attached to the bounce.
    /// </summary>
    [JsonPropertyName("reportingMTA")]
    public string? ReportingMta { get; init; }

    /// <summary>The recipients of the original mail that bounced.</summary>
    [JsonPropertyName("bouncedRecipients")]
    public BouncedRecipient[] BouncedRecipients { get; init; } = [];
}
