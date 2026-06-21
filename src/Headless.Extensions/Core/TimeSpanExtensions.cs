// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

/// <summary>Extension methods for <see cref="TimeSpan"/>: clamping, min/max, and building cancellation token sources.</summary>
[PublicAPI]
public static class TimeSpanExtensions
{
    /// <summary>Clamps <paramref name="timeSpan"/> to the inclusive range defined by <paramref name="min"/> and <paramref name="max"/>.</summary>
    /// <param name="timeSpan">The value to clamp.</param>
    /// <param name="min">The lower bound returned when <paramref name="timeSpan"/> is below it.</param>
    /// <param name="max">The upper bound returned when <paramref name="timeSpan"/> is above it.</param>
    /// <returns>
    /// <paramref name="min"/> if <paramref name="timeSpan"/> is less than it, <paramref name="max"/> if greater than it,
    /// otherwise <paramref name="timeSpan"/> unchanged.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static TimeSpan Clamp(this TimeSpan timeSpan, TimeSpan min, TimeSpan max)
    {
        return timeSpan < min ? min
            : timeSpan > max ? max
            : timeSpan;
    }

    /// <summary>Returns the smaller of <paramref name="source"/> and <paramref name="other"/>.</summary>
    /// <param name="source">The first value to compare.</param>
    /// <param name="other">The second value to compare.</param>
    /// <returns>Whichever of the two <see cref="TimeSpan"/> values is shorter.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static TimeSpan Min(this TimeSpan source, TimeSpan other)
    {
        return source.Ticks > other.Ticks ? other : source;
    }

    /// <summary>Returns the larger of <paramref name="source"/> and <paramref name="other"/>.</summary>
    /// <param name="source">The first value to compare.</param>
    /// <param name="other">The second value to compare.</param>
    /// <returns>Whichever of the two <see cref="TimeSpan"/> values is longer.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static TimeSpan Max(this TimeSpan source, TimeSpan other)
    {
        return source.Ticks < other.Ticks ? other : source;
    }

    /// <summary>Creates a <see cref="CancellationTokenSource"/> that cancels after <paramref name="timeout"/> elapses.</summary>
    /// <param name="timeout">
    /// The timeout after which the source cancels. <see cref="TimeSpan.Zero"/> produces an already-canceled source,
    /// and a negative timeout produces a source that never cancels on its own.
    /// </param>
    /// <returns>A new <see cref="CancellationTokenSource"/> configured according to <paramref name="timeout"/>.</returns>
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

    /// <summary>
    /// Creates a <see cref="CancellationTokenSource"/> linked to <paramref name="token"/> that also cancels after
    /// <paramref name="timeout"/> elapses.
    /// </summary>
    /// <param name="timeout">
    /// The timeout after which the source cancels. <see cref="TimeSpan.Zero"/> produces an already-canceled source;
    /// a negative timeout produces a source that cancels only when <paramref name="token"/> is canceled.
    /// </param>
    /// <param name="token">A token to link to; cancelling it also cancels the returned source.</param>
    /// <returns>A new <see cref="CancellationTokenSource"/> linked to <paramref name="token"/> and configured per <paramref name="timeout"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static CancellationTokenSource ToCancellationTokenSource(this TimeSpan timeout, CancellationToken token)
    {
        if (timeout == TimeSpan.Zero)
        {
            var source = new CancellationTokenSource();
            source.Cancel();

            return source;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        if (timeout.Ticks > 0)
        {
            cts.CancelAfter(timeout);
        }

        return cts;
    }

    /// <summary>Creates a <see cref="CancellationTokenSource"/> from an optional <paramref name="timeout"/>.</summary>
    /// <param name="timeout">
    /// The timeout after which the source cancels; when <see langword="null"/>, a source that never times out is returned.
    /// </param>
    /// <returns>A new <see cref="CancellationTokenSource"/> configured per <paramref name="timeout"/>, or one that never cancels when it is <see langword="null"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static CancellationTokenSource ToCancellationTokenSource(this TimeSpan? timeout)
    {
        return timeout.HasValue ? timeout.Value.ToCancellationTokenSource() : new CancellationTokenSource();
    }

    /// <summary>Creates a <see cref="CancellationTokenSource"/> from an optional <paramref name="timeout"/>, falling back to <paramref name="defaultTimeout"/>.</summary>
    /// <param name="timeout">The timeout after which the source cancels; when <see langword="null"/>, <paramref name="defaultTimeout"/> is used.</param>
    /// <param name="defaultTimeout">The timeout to apply when <paramref name="timeout"/> is <see langword="null"/>.</param>
    /// <returns>A new <see cref="CancellationTokenSource"/> configured from <paramref name="timeout"/> or <paramref name="defaultTimeout"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static CancellationTokenSource ToCancellationTokenSource(this TimeSpan? timeout, TimeSpan defaultTimeout)
    {
        return (timeout ?? defaultTimeout).ToCancellationTokenSource();
    }
}
