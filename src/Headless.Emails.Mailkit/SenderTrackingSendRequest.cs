// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MailKit.Net.Smtp;
using MimeKit;

namespace Headless.Emails.Mailkit;

/// <summary>
/// One SMTP send attempt that records whether the server accepted <c>MAIL FROM</c>. MailKit sends <c>DATA</c> or
/// <c>BDAT</c> only after that reply, so while <see cref="SenderAccepted"/> is <see langword="false"/> the server
/// cannot hold any part of the message. The envelope comes from the message headers, as MailKit's
/// <c>SendAsync(MimeMessage)</c> builds it.
/// </summary>
/// <remarks>Create one instance per attempt: the flag describes the attempt that just failed or succeeded.</remarks>
internal sealed class SenderTrackingSendRequest(MimeMessage message) : SmtpSendRequest(message)
{
    /// <summary>Gets a value indicating whether the server accepted <c>MAIL FROM</c> during this attempt.</summary>
    public bool SenderAccepted { get; private set; }

    public override void OnSenderAccepted(MailboxAddress sender, SmtpResponse response)
    {
        SenderAccepted = true;
        base.OnSenderAccepted(sender, response);
    }
}
