// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Messaging.Retry;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct MessagingRetryDecision
{
    internal enum Kind
    {
        Stop,
        Exhausted,
        Continue,
    }

    public Kind Outcome { get; init; }

    public TimeSpan Delay { get; init; }

    /// <summary>
    /// Whether the decision ends a request whose caller already stopped waiting. Nobody can use a reply to it, and its
    /// expiry is no consumer failure, so it fires neither the exhausted callback, a fault reply, nor a breaker report.
    /// </summary>
    public bool IsRequestExpired { get; init; }

    public static MessagingRetryDecision Stop { get; } = new() { Outcome = Kind.Stop };

    public static MessagingRetryDecision RequestExpired { get; } =
        new() { Outcome = Kind.Stop, IsRequestExpired = true };

    public static MessagingRetryDecision Exhausted { get; } = new() { Outcome = Kind.Exhausted };

    public static MessagingRetryDecision Continue(TimeSpan delay)
    {
        return new() { Outcome = Kind.Continue, Delay = delay };
    }
}
