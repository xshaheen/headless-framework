// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using MailKit.Net.Smtp;
using MimeKit;

namespace Headless.Emails.Mailkit;

/// <summary>
/// An <see cref="SmtpClient"/> that remembers when it last completed a send and whether the server accepted the
/// sender of the send in progress, so the sender can skip the idle probe on a recently used connection and tell
/// whether a failed send could have reached the server's <c>DATA</c> phase.
/// </summary>
internal class PooledSmtpClient : SmtpClient
{
    private long _lastSendTimestamp;

    /// <summary>
    /// Gets a value indicating whether the server accepted <c>MAIL FROM</c> since the last
    /// <see cref="BeginSend"/>. MailKit sends <c>DATA</c> or <c>BDAT</c> only after that reply, so while it is
    /// <see langword="false"/> the server cannot hold any part of the message.
    /// </summary>
    public bool SenderAccepted { get; private set; }

    /// <summary>Clears <see cref="SenderAccepted"/> before a send attempt.</summary>
    public void BeginSend()
    {
        SenderAccepted = false;
    }

    /// <summary>Records that a send just completed on this connection.</summary>
    public void MarkSendCompleted()
    {
        _lastSendTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Returns whether a send completed on this connection less than <paramref name="threshold"/> ago. Idle time
    /// is real time (a server drops a session by wall-clock idleness), so it reads <see cref="Stopwatch"/>, not
    /// the application's <see cref="TimeProvider"/>.
    /// </summary>
    public bool WasUsedWithin(TimeSpan threshold)
    {
        return _lastSendTimestamp != 0 && Stopwatch.GetElapsedTime(_lastSendTimestamp) < threshold;
    }

    protected override void OnSenderAccepted(MimeMessage message, MailboxAddress mailbox, SmtpResponse response)
    {
        SenderAccepted = true;
        base.OnSenderAccepted(message, mailbox, response);
    }
}
