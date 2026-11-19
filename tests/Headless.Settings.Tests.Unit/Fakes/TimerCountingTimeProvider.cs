// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Fakes;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that counts the timers created against it, so a test can wait until code running
/// on another thread has registered its next delay before advancing time. Advancing first would fire nothing, because
/// the delay is created against the later time.
/// </summary>
internal sealed class TimerCountingTimeProvider : FakeTimeProvider
{
    private int _timersCreated;

    public int TimersCreated => Volatile.Read(ref _timersCreated);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _timersCreated);

        return timer;
    }

    public Task WaitForTimersAsync(int count, CancellationToken cancellationToken) =>
        WaitUntilAsync(() => TimersCreated >= count, cancellationToken);

    /// <summary>
    /// Waits until <paramref name="condition"/> holds, failing after five seconds instead of hanging. The poll runs on
    /// the real clock: polling on this fake one would stall until the test advanced it.
    /// </summary>
    public static Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken,
        [CallerArgumentExpression(nameof(condition))] string? conditionExpression = null
    ) =>
        Eventually.UntilAsync(
            condition,
            TimeSpan.FromSeconds(5),
            cancellationToken: cancellationToken,
            conditionExpression: conditionExpression
        );
}
