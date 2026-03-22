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
    private TaskCompletionSource<bool> _gate = _CreateCompletedGate();
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

    public ValueTask PauseAsync()
    {
        lock (_lock)
        {
            if (_disposed || _paused) return ValueTask.CompletedTask;
            _paused = true;
            _gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ResumeAsync()
    {
        TaskCompletionSource<bool> gateToComplete;
        lock (_lock)
        {
            if (_disposed || !_paused) return ValueTask.CompletedTask;
            _paused = false;
            gateToComplete = _gate;
        }
        gateToComplete.TrySetResult(true);
        return ValueTask.CompletedTask;
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
