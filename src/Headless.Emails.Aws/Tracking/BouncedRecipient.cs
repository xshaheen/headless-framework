// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

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
