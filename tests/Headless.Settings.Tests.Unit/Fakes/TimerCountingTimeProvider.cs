// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

    /// <summary>Waits until <paramref name="condition"/> holds, failing after five seconds instead of hanging.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        while (!condition())
        {
            try
            {
                await Task.Delay(5, timeout.Token);
            }
            catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The awaited condition did not hold within five seconds.", e);
            }
        }
    }
}
