// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>A recipient whose delivery was delayed, as reported in <see cref="SesDeliveryDelayEvent.DelayedRecipients"/>.</summary>
[PublicAPI]
public sealed record DelayedRecipient
{
    /// <summary>The email address that resulted in the delivery of the message being delayed.</summary>
    [JsonPropertyName("emailAddress")]
    public string EmailAddress { get; init; } = null!;

    /// <summary>The SMTP status code associated with the delivery delay.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = null!;

    /// <summary>The diagnostic code provided by the receiving Message Transfer Agent (MTA).</summary>
    [JsonPropertyName("diagnosticCode")]
    public string DiagnosticCode { get; init; } = null!;
}
