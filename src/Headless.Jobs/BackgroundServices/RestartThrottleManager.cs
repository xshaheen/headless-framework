// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Jobs;

internal sealed class RestartThrottleManager(Action onRestartTriggered, TimeProvider timeProvider) : IDisposable
{
    private readonly Lock _lock = new();
    private ITimer? _debounceTimer;
    private volatile bool _restartPending;
    private bool _disposed;

    private long _pendingSinceTimestamp;

    private static readonly TimeSpan _DebounceWindow = TimeSpan.FromMilliseconds(50);

    // Requests spaced under the debounce window would otherwise postpone the restart indefinitely, leaving a newly
    // earlier job waiting for the fallback poll. Five windows keeps bursts coalesced while bounding the delay.
    private static readonly TimeSpan _MaxWait = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Schedules a restart notification after the debounce window, but no later than the max wait measured from
    /// the first request still pending.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The manager has been disposed.</exception>
    public void RequestRestart()
    {
        lock (_lock)
        {
            Ensure.NotDisposed(_disposed, this);

            if (!_restartPending)
            {
                _pendingSinceTimestamp = timeProvider.GetTimestamp();
                _restartPending = true;
            }

            var remainingWait = _MaxWait - timeProvider.GetElapsedTime(_pendingSinceTimestamp);
            var dueTime =
                remainingWait <= TimeSpan.Zero ? TimeSpan.Zero
                : remainingWait < _DebounceWindow ? remainingWait
                : _DebounceWindow;

            // Create timer only when first needed
            if (_debounceTimer == null)
            {
                _debounceTimer = timeProvider.CreateTimer(
                    _OnTimerCallback,
                    state: null,
                    dueTime,
                    Timeout.InfiniteTimeSpan
                );
            }
            else
            {
                // Just reset existing timer
                _debounceTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void _OnTimerCallback(object? state)
    {
        var shouldInvoke = false;

        lock (_lock)
        {
            if (!_disposed && _restartPending)
            {
                _restartPending = false;
                shouldInvoke = true;
            }
        }

        if (shouldInvoke)
        {
            onRestartTriggered();
        }
    }

    public void Dispose()
    {
        ITimer? timer;

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _restartPending = false;
            timer = _debounceTimer;
            _debounceTimer = null;
        }

        timer?.Dispose();
    }
}
