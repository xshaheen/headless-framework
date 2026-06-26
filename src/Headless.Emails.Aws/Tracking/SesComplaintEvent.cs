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

/// <summary>A recipient that may have submitted a complaint, as reported in <see cref="SesComplaintEvent.ComplainedRecipients"/>.</summary>
[PublicAPI]
public sealed record ComplainedRecipient
{
    /// <summary>The email address of the recipient.</summary>
    /// <remarks>
    /// Most ISPs redact the addresses of recipients who submit complaints, so this list includes everyone
    /// sent the email whose address is on the domain that issued the complaint notification.
    /// </remarks>
    [JsonPropertyName("emailAddress")]
    public string EmailAddress { get; init; } = null!;
}

/// <summary>
/// The known values of <see cref="SesComplaintEvent.ComplaintFeedbackType"/>, as assigned by the reporting
/// ISP per the IANA MARF parameters registry.
/// </summary>
[PublicAPI]
public static class ComplaintFeedbackTypes
{
    /// <summary>Unsolicited email or some other kind of email abuse.</summary>
    public const string Abuse = "abuse";

    /// <summary>Email authentication failure report.</summary>
    public const string AuthFailure = "auth-failure";

    /// <summary>Some kind of fraud or phishing activity.</summary>
    public const string Fraud = "fraud";

    /// <summary>The reporting entity does not consider the message to be spam.</summary>
    public const string NotSpam = "not-spam";

    /// <summary>A virus was found in the originating message.</summary>
    public const string Virus = "virus";

    /// <summary>Any other feedback that does not fit a registered type.</summary>
    public const string Other = "other";
}

/// <summary>The known values of <see cref="SesComplaintEvent.ComplaintSubType"/>.</summary>
[PublicAPI]
public static class ComplaintSubTypes
{
    /// <summary>SES accepted the message but suppressed sending because the address is on the account-level suppression list.</summary>
    public const string OnAccountSuppressionList = "OnAccountSuppressionList";

    /// <summary>SES accepted the message but suppressed sending because the address is on the tenant-level suppression list.</summary>
    public const string OnTenantSuppressionList = "OnTenantSuppressionList";
}
