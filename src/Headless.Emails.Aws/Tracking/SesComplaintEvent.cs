// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>Information about a <c>Complaint</c> event.</summary>
[PublicAPI]
public sealed record SesComplaintEvent
{
    /// <summary>The recipients that may have submitted the complaint.</summary>
    [JsonPropertyName("complainedRecipients")]
    public ComplainedRecipient[] ComplainedRecipients { get; init; } = [];

    /// <summary>A unique ID for the complaint.</summary>
    [JsonPropertyName("feedbackId")]
    public string FeedbackId { get; init; } = null!;

    /// <summary>The date and time when the ISP sent the complaint notification.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The subtype of the complaint. <see langword="null"/>, <c>OnAccountSuppressionList</c>, or
    /// <c>OnTenantSuppressionList</c> — the suppression-list values mean SES accepted the message but did
    /// not attempt to send it because the address was on the corresponding suppression list. See
    /// <see cref="ComplaintSubTypes"/> for the known values.
    /// </summary>
    [JsonPropertyName("complaintSubType")]
    public string? ComplaintSubType { get; init; }

    /// <summary>
    /// The value of the <c>Feedback-Type</c> field from the feedback report received from the ISP. Present
    /// only when a feedback report is attached. See <see cref="ComplaintFeedbackTypes"/> for known values.
    /// </summary>
    [JsonPropertyName("complaintFeedbackType")]
    public string? ComplaintFeedbackType { get; init; }

    /// <summary>
    /// The value of the <c>Arrival-Date</c> or <c>Received-Date</c> field from the feedback report. May be
    /// absent even when a feedback report is attached.
    /// </summary>
    [JsonPropertyName("arrivalDate")]
    public DateTimeOffset? ArrivalDate { get; init; }

    /// <summary>
    /// The value of the <c>User-Agent</c> field from the feedback report — the name and version of the
    /// system that generated the report. Present only when a feedback report is attached.
    /// </summary>
    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; init; }
}
