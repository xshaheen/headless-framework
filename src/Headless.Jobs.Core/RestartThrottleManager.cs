// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

public sealed class RestartThrottleManager : IDisposable
{
    private readonly Action _onRestartTriggered;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _lock = new();
    private ITimer? _debounceTimer;
    private volatile bool _restartPending;
    private bool _disposed;

    private readonly TimeSpan _debounceWindow = TimeSpan.FromMilliseconds(50);

    public RestartThrottleManager(Action onRestartTriggered)
        : this(onRestartTriggered, TimeProvider.System) { }

    internal RestartThrottleManager(Action onRestartTriggered, TimeProvider timeProvider)
    {
        _onRestartTriggered = onRestartTriggered;
        _timeProvider = timeProvider;
    }

    /// <summary>Schedules a restart notification after the debounce window.</summary>
    /// <exception cref="ObjectDisposedException">The manager has been disposed.</exception>
    public void RequestRestart()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _restartPending = true;

            // Create timer only when first needed
            if (_debounceTimer == null)
            {
                _debounceTimer = _timeProvider.CreateTimer(
                    _OnTimerCallback,
                    state: null,
                    _debounceWindow,
                    Timeout.InfiniteTimeSpan
                );
            }
            else
            {
                // Just reset existing timer
                _debounceTimer.Change(_debounceWindow, Timeout.InfiniteTimeSpan);
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
            _onRestartTriggered();
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
