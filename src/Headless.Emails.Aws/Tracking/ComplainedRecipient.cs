// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

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
