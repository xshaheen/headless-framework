// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// Thread-safe pause/resume gate for consumer clients.
/// Callers await <see cref="WaitIfPausedAsync"/> in their consume loop;
/// the circuit breaker calls <see cref="PauseAsync"/>/<see cref="ResumeAsync"/> on state transitions.
/// </summary>
internal sealed class ConsumerPauseGate
{
    private readonly Lock _lock = new();
    private volatile TaskCompletionSource<bool> _gate = _CreateCompletedGate();
    private bool _paused;
    private bool _disposed;

    public bool IsPaused { get { lock (_lock) { return _paused; } } }

    public ValueTask WaitIfPausedAsync(CancellationToken ct)
    {
        var gate = _gate; // snapshot under volatile read
        return gate.Task.IsCompleted
            ? ValueTask.CompletedTask
            : new ValueTask(gate.Task.WaitAsync(ct));
    }

    /// <summary>
    /// Transitions the gate to the paused state.
    /// Returns <c>true</c> if this call actually transitioned from unpaused to paused;
    /// <c>false</c> if already paused or disposed.
    /// </summary>
    public ValueTask<bool> PauseAsync()
    {
        lock (_lock)
        {
            if (_disposed || _paused) return new ValueTask<bool>(false);
            _paused = true;
            _gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return new ValueTask<bool>(true);
    }

    /// <summary>
    /// Transitions the gate to the resumed state.
    /// Returns <c>true</c> if this call actually transitioned from paused to resumed;
    /// <c>false</c> if already resumed or disposed.
    /// </summary>
    public ValueTask<bool> ResumeAsync()
    {
        TaskCompletionSource<bool> gateToComplete;
        lock (_lock)
        {
            if (_disposed || !_paused) return new ValueTask<bool>(false);
            _paused = false;
            gateToComplete = _gate;
        }
        gateToComplete.TrySetResult(true);
        return new ValueTask<bool>(true);
    }

    public void Release()
    {
        TaskCompletionSource<bool> gateToComplete;
        lock (_lock)
        {
            _disposed = true;
            _paused = false;
            gateToComplete = _gate;
        }
        gateToComplete.TrySetResult(true);
    }

    private static TaskCompletionSource<bool> _CreateCompletedGate()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult(true);
        return tcs;
    }
}
