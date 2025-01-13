// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class TimeSpanExtensions
{
    [SystemPure]
    [JetBrainsPure]
    public static TimeSpan Clamp(this TimeSpan timeSpan, TimeSpan min, TimeSpan max)
    {
        return timeSpan < min ? min
            : timeSpan > max ? max
            : timeSpan;
    }
}
