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
    /// Whether the decision ends a delivery nobody can use anymore: a request whose caller already stopped waiting,
    /// or a message past its delivery deadline. Nobody can consume the result, and the expiry is no consumer failure,
    /// so it fires neither the exhausted callback, a fault reply, nor a breaker report.
    /// </summary>
    public bool IsSilentExpiry { get; init; }

    public static MessagingRetryDecision Stop { get; } = new() { Outcome = Kind.Stop };

    public static MessagingRetryDecision SilentExpiry { get; } = new() { Outcome = Kind.Stop, IsSilentExpiry = true };

    public static MessagingRetryDecision Exhausted { get; } = new() { Outcome = Kind.Exhausted };

    public static MessagingRetryDecision Continue(TimeSpan delay)
    {
        return new() { Outcome = Kind.Continue, Delay = delay };
    }
}
