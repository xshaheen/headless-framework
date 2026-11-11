// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Nito.AsyncEx;

namespace Headless.Jobs.BackgroundServices;

/// <summary>
/// Starts a fallback pass before the fallback period ends. The scheduler's peek sees only rows due within the last
/// second; an older row is claimed by the fallback sweep alone. A scheduler poll that lands more than that second after
/// the wake it slept toward (a starved thread pool, a GC pause, a slow poll, a dispatch that waited for a worker) has
/// let the rows it woke for age out of the peek, and without this signal they wait up to a whole
/// <see cref="SchedulerOptionsBuilder.FallbackIntervalChecker"/>.
/// </summary>
internal sealed class JobsFallbackWake
{
    private readonly AsyncAutoResetEvent _signal = new();

    /// <summary>Starts the next fallback pass now, or right after the pass that is running.</summary>
    public void Request() => _signal.Set();

    /// <summary>Waits until a pass is requested or <paramref name="period"/> has passed on <paramref name="timeProvider"/>.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task WaitAsync(TimeSpan period, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var requested = _signal.WaitAsync(waitCts.Token);
        var elapsed = timeProvider.Delay(period, waitCts.Token);
        // Whichever ends first ends the wait; the loser is cancelled below and its outcome does not matter.
        _ = await Task.WhenAny(requested, elapsed).ConfigureAwait(false);

        // Cancelling the loser removes its waiter, so a request that arrives after this wait ends is kept for the next
        // one. A request that lands between the two is consumed, but the pass the caller runs next still follows it.
        await waitCts.CancelAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
