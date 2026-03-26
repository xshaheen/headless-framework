// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Messaging.Internal;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct RetryDecision(bool ShouldRetry, TimeSpan Delay)
{
    public static RetryDecision Stop => new(false, TimeSpan.Zero);

    public static RetryDecision Continue(TimeSpan delay) => new(true, delay);
}
