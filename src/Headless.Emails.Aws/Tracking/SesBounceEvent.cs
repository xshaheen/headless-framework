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

/// <summary>A recipient whose email address produced a bounce, as reported in <see cref="SesBounceEvent.BouncedRecipients"/>.</summary>
[PublicAPI]
public sealed record BouncedRecipient
{
    /// <summary>
    /// The email address of the recipient. If a DSN is available, this is the value of the
    /// <c>Final-Recipient</c> field from the DSN.
    /// </summary>
    [JsonPropertyName("emailAddress")]
    public string EmailAddress { get; init; } = null!;

    /// <summary>
    /// The value of the <c>Action</c> field from the DSN — the action performed by the reporting MTA as a
    /// result of its attempt to deliver the message. Present only when a DSN was attached to the bounce.
    /// </summary>
    [JsonPropertyName("action")]
    public string? Action { get; init; }

    /// <summary>
    /// The value of the <c>Status</c> field from the DSN — the per-recipient transport-independent status
    /// code indicating delivery status. Present only when a DSN was attached to the bounce.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>
    /// The status code issued by the reporting MTA — the value of the <c>Diagnostic-Code</c> field from the
    /// DSN. May be absent even when a DSN is attached.
    /// </summary>
    [JsonPropertyName("diagnosticCode")]
    public string? DiagnosticCode { get; init; }
}

/// <summary>The possible values of <see cref="SesBounceEvent.BounceType"/>.</summary>
[PublicAPI]
public static class BounceTypes
{
    /// <summary>SES was unable to determine a specific bounce reason.</summary>
    public const string Undetermined = "Undetermined";

    /// <summary>A hard bounce. Remove the recipient from your mailing list.</summary>
    public const string Permanent = "Permanent";

    /// <summary>A soft bounce that SES has stopped retrying. A later send may succeed.</summary>
    public const string Transient = "Transient";
}

/// <summary>The possible values of <see cref="SesBounceEvent.BounceSubType"/>.</summary>
[PublicAPI]
public static class BounceSubTypes
{
    /// <summary>SES was unable to determine a specific bounce reason.</summary>
    public const string Undetermined = "Undetermined";

    /// <summary>A general bounce.</summary>
    public const string General = "General";

    /// <summary>A permanent hard bounce because the target email address does not exist.</summary>
    public const string NoEmail = "NoEmail";

    /// <summary>SES suppressed sending because the address has a recent history of bouncing as invalid.</summary>
    public const string Suppressed = "Suppressed";

    /// <summary>SES suppressed sending because the address is on the account-level suppression list.</summary>
    public const string OnAccountSuppressionList = "OnAccountSuppressionList";

    /// <summary>SES suppressed sending because the address did not meet your email validation threshold.</summary>
    public const string EmailValidationSuppressed = "EmailValidationSuppressed";

    /// <summary>SES suppressed sending because the address is on the tenant-level suppression list.</summary>
    public const string OnTenantSuppressionList = "OnTenantSuppressionList";

    /// <summary>The recipient's mailbox is full.</summary>
    public const string MailboxFull = "MailboxFull";

    /// <summary>The message was too large.</summary>
    public const string MessageTooLarge = "MessageTooLarge";

    /// <summary>SES could not deliver the email within the time specified by the sender.</summary>
    public const string CustomTimeoutExceeded = "CustomTimeoutExceeded";

    /// <summary>The content of the message was rejected.</summary>
    public const string ContentRejected = "ContentRejected";

    /// <summary>An attachment was rejected.</summary>
    public const string AttachmentRejected = "AttachmentRejected";
}
