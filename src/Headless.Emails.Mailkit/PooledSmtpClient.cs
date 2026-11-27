// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using MailKit.Net.Smtp;

namespace Headless.Emails.Mailkit;

/// <summary>
/// An <see cref="SmtpClient"/> that remembers when it last completed a send, so the sender can skip the idle
/// probe on a recently used connection.
/// </summary>
internal class PooledSmtpClient : SmtpClient
{
    private long _lastSendTimestamp;

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
}
