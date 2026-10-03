// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

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
