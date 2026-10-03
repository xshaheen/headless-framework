// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>The possible values of <see cref="SesDeliveryDelayEvent.DelayType"/>.</summary>
[PublicAPI]
public static class DelayTypes
{
    /// <summary>An internal Amazon SES issue caused the message to be delayed.</summary>
    public const string InternalFailure = "InternalFailure";

    /// <summary>A generic failure occurred during the SMTP conversation.</summary>
    public const string General = "General";

    /// <summary>The recipient's mailbox is full and is unable to receive additional messages.</summary>
    public const string MailboxFull = "MailboxFull";

    /// <summary>The recipient's mail server detected a large amount of unsolicited email from your account.</summary>
    public const string SpamDetected = "SpamDetected";

    /// <summary>A temporary issue with the recipient's email server is preventing delivery of the message.</summary>
    public const string RecipientServerError = "RecipientServerError";

    /// <summary>The sending IP address is being blocked or throttled by the recipient's email provider.</summary>
    public const string IpFailure = "IPFailure";

    /// <summary>A temporary communication failure occurred during the SMTP conversation with the provider.</summary>
    public const string TransientCommunicationFailure = "TransientCommunicationFailure";

    /// <summary>
    /// SES was unable to look up the DNS hostname for your IP addresses. Only occurs when you use Bring
    /// Your Own IP.
    /// </summary>
    public const string ByoIpHostNameLookupUnavailable = "BYOIPHostNameLookupUnavailable";

    /// <summary>SES could not determine the reason for the delivery delay.</summary>
    public const string Undetermined = "Undetermined";
}
