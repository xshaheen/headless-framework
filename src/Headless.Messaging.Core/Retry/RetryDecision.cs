// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Messaging.Retry;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct RetryDecision
{
    public enum Kind
    {
        Stop,
        Exhausted,
        Continue,
    }

    public Kind Outcome { get; init; }

    public TimeSpan Delay { get; init; }

    public static RetryDecision Stop { get; } = new() { Outcome = Kind.Stop };

    public static RetryDecision Exhausted { get; } = new() { Outcome = Kind.Exhausted };

    public static RetryDecision Continue(TimeSpan delay) => new() { Outcome = Kind.Continue, Delay = delay };

    public bool ShouldRetry => Outcome == Kind.Continue;
}
