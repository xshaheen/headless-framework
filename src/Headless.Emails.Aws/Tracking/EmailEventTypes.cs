// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>
/// The string values Amazon SES uses for the <see cref="EmailTrackingNotification.EventType"/> field
/// when publishing email sending events through a configuration set event destination.
/// </summary>
[PublicAPI]
public static class EmailEventTypes
{
    /// <summary>The recipient's mail server accepted the email.</summary>
    public const string Delivery = "Delivery";

    /// <summary>The send request was successful and SES will attempt to deliver the message.</summary>
    public const string Send = "Send";

    /// <summary>SES accepted the email, determined that it contained a virus, and rejected it.</summary>
    public const string Reject = "Reject";

    /// <summary>The recipient opened the email and the embedded open-tracking pixel loaded.</summary>
    public const string Open = "Open";

    /// <summary>The recipient clicked one or more links in the email.</summary>
    public const string Click = "Click";

    /// <summary>The recipient's mail server permanently or temporarily rejected the email.</summary>
    public const string Bounce = "Bounce";

    /// <summary>The recipient marked the email as spam.</summary>
    public const string Complaint = "Complaint";

    /// <summary>SES could not render the template referenced by a templated send.</summary>
    public const string RenderingFailure = "Rendering Failure";

    /// <summary>SES could not deliver the email to the recipient's mail server within the expected window.</summary>
    public const string DeliveryDelay = "DeliveryDelay";
}
