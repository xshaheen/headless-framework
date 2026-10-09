// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Transport;

/// <summary>
/// Tracks the handler tasks a consumer client has dispatched and not yet finished, so the client's shutdown can wait for
/// them before it disposes the channel, connection, or semaphore their settlement needs.
/// </summary>
/// <remarks>
/// A handler still running when the shutdown budget runs out settles against disposed resources and fails; its message
/// is redelivered, which is the at-least-once contract every transport already gives a crash. The drain only narrows
/// that window for a graceful stop.
/// </remarks>
internal sealed class InFlightHandlerTracker
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Task, object?> _handlers = [];
    private bool _accepting = true;

    /// <summary>The number of tracked handlers that have not finished.</summary>
    /// <remarks>
    /// Counts by completion rather than by removal, because a handler's removal continuation can run after a waiter on
    /// the same handler already resumed.
    /// </remarks>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _handlers.Keys.Count(static task => !task.IsCompleted);
            }
        }
    }

    /// <summary>
    /// Tracks <paramref name="handler"/> until it completes, with an optional <paramref name="tag"/> that
    /// <see cref="Snapshot"/> can filter on, such as the partition the handler's message came from.
    /// </summary>
    /// <remarks>Tracks even after <see cref="DrainAsync"/> began, so a handler that started late is still waited for.</remarks>
    public void Track(Task handler, object? tag = null)
    {
        _ = Argument.IsNotNull(handler);

        lock (_lock)
        {
            _handlers[handler] = tag;
        }

        _RemoveOnCompletion(handler);
    }

    /// <summary>
    /// Tracks <paramref name="handler"/> unless <see cref="DrainAsync"/> or <see cref="StopAccepting"/> already ran.
    /// </summary>
    /// <remarks>
    /// The check and the registration share one lock with the drain's snapshot, so a handler either registers before the
    /// drain looks or is refused; none starts unseen between the two. A client that can leave a refused delivery
    /// unsettled, for the broker to return when the channel closes, uses this instead of <see cref="Track"/>.
    /// </remarks>
    /// <returns><see langword="true"/> when tracked; <see langword="false"/> when the tracker no longer accepts handlers.</returns>
    public bool TryTrack(Task handler)
    {
        _ = Argument.IsNotNull(handler);

        lock (_lock)
        {
            if (!_accepting)
            {
                return false;
            }

            _handlers[handler] = null;
        }

        _RemoveOnCompletion(handler);
        return true;
    }

    /// <summary>Makes every later <see cref="TryTrack"/> call refuse its handler.</summary>
    public void StopAccepting()
    {
        lock (_lock)
        {
            _accepting = false;
        }
    }

    /// <summary>The unfinished handlers whose tag matches <paramref name="match"/>, or all of them when it is null.</summary>
    public Task[] Snapshot(Func<object?, bool>? match = null)
    {
        lock (_lock)
        {
            return match is null
                ? [.. _handlers.Keys]
                : [.. _handlers.Where(pair => match(pair.Value)).Select(static pair => pair.Key)];
        }
    }

    /// <summary>
    /// Stops accepting new handlers, then waits up to <paramref name="timeout"/> for every tracked handler, including one
    /// tracked while the drain runs, to finish.
    /// </summary>
    /// <param name="timeout">The remaining shutdown budget; zero or less fails at once when a handler is still running.</param>
    /// <param name="timeProvider">The clock the wait is measured on.</param>
    /// <remarks>A drained handler's fault propagates, as <c>Task.WhenAll</c> reports it, once every snapshot handler ends.</remarks>
    /// <exception cref="TimeoutException">A handler was still running when <paramref name="timeout"/> elapsed.</exception>
    public async Task DrainAsync(TimeSpan timeout, TimeProvider timeProvider)
    {
        Argument.IsNotNull(timeProvider);

        StopAccepting();
        var startedAt = timeProvider.GetTimestamp();

        // Snapshots repeat until none is left: a delivery that already passed its own shutdown check can still start a
        // handler after the first snapshot. Completed handlers can linger until their removal continuation runs, so only
        // unfinished ones count, which keeps the loop from spinning on them.
        Task[] inFlight;
        while ((inFlight = _UnfinishedSnapshot()).Length > 0)
        {
            var remaining = timeout - timeProvider.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException("The shared messaging shutdown deadline has expired.");
            }

            await Task.WhenAll(inFlight)
                .WaitAsync(remaining, timeProvider, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private Task[] _UnfinishedSnapshot()
    {
        lock (_lock)
        {
            return [.. _handlers.Keys.Where(static task => !task.IsCompleted)];
        }
    }

    private void _RemoveOnCompletion(Task handler)
    {
        _ = handler.ContinueWith(
            static (completed, state) =>
            {
                var tracker = (InFlightHandlerTracker)state!;
                lock (tracker._lock)
                {
                    tracker._handlers.Remove(completed);
                }
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }
}
