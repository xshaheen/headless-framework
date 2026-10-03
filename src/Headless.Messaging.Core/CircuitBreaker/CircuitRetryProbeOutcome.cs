// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Messaging.CircuitBreaker;

internal enum CircuitRetryProbeOutcomeKind
{
    Closed,
    Reopened,
    Uncertain,
}

[StructLayout(LayoutKind.Auto)]
internal readonly record struct CircuitRetryProbeOutcome(
    CircuitRetryProbeOutcomeKind Kind,
    DateTimeOffset? NextProbeAt
);
