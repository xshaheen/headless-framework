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

    [SystemPure]
    [JetBrainsPure]
    public static TimeSpan Min(this TimeSpan source, TimeSpan other)
    {
        return source.Ticks > other.Ticks ? other : source;
    }

    [SystemPure]
    [JetBrainsPure]
    public static TimeSpan Max(this TimeSpan source, TimeSpan other)
    {
        return source.Ticks < other.Ticks ? other : source;
    }

    [SystemPure]
    [JetBrainsPure]
    public static CancellationTokenSource ToCancellationTokenSource(this TimeSpan timeout)
    {
        if (timeout == TimeSpan.Zero)
        {
            var source = new CancellationTokenSource();
            source.Cancel();

            return source;
        }

        return timeout.Ticks > 0 ? new CancellationTokenSource(timeout) : new CancellationTokenSource();
    }

    [SystemPure]
    [JetBrainsPure]
    public static CancellationTokenSource ToCancellationTokenSource(this TimeSpan? timeout)
    {
        return timeout.HasValue ? timeout.Value.ToCancellationTokenSource() : new CancellationTokenSource();
    }

    [SystemPure]
    [JetBrainsPure]
    public static CancellationTokenSource ToCancellationTokenSource(this TimeSpan? timeout, TimeSpan defaultTimeout)
    {
        return (timeout ?? defaultTimeout).ToCancellationTokenSource();
    }
}
