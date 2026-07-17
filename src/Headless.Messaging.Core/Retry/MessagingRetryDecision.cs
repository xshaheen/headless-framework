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

    public static MessagingRetryDecision Stop { get; } = new() { Outcome = Kind.Stop };

    public static MessagingRetryDecision Exhausted { get; } = new() { Outcome = Kind.Exhausted };

    public static MessagingRetryDecision Continue(TimeSpan delay)
    {
        return new() { Outcome = Kind.Continue, Delay = delay };
    }
}
