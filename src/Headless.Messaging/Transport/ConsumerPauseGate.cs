// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// Thread-safe pause/resume gate for consumer clients.
/// Callers await <see cref="WaitIfPausedAsync"/> in their consume loop;
/// the circuit breaker calls <see cref="PauseAsync"/>/<see cref="ResumeAsync"/> on state transitions.
/// </summary>
/// <remarks>
/// A consumer client backs <see cref="IConsumerClient.PauseAsync"/> and <see cref="IConsumerClient.ResumeAsync"/> with
/// one gate and calls <see cref="Release"/> when it shuts down, so a loop parked on a paused gate wakes and observes its
/// own cancellation instead of hanging the shutdown.
/// </remarks>
public sealed class ConsumerPauseGate
{
    private readonly Lock _lock = new();
    private volatile TaskCompletionSource _gate = _CreateCompletedGate();
    private bool _paused;
    private bool _disposed;

    /// <summary>Gets whether the gate is paused.</summary>
    public bool IsPaused
    {
        get
        {
            lock (_lock)
            {
                return _paused;
            }
        }
    }

    /// <summary>Completes at once while the gate is open; otherwise waits until it resumes or is released.</summary>
    /// <param name="cancellationToken">Ends the wait early, with <see cref="OperationCanceledException"/>.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled while paused.</exception>
    public ValueTask WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        var gate = _gate; // snapshot under volatile read
        return gate.Task.IsCompleted ? ValueTask.CompletedTask : new ValueTask(gate.Task.WaitAsync(cancellationToken));
    }

    /// <summary>
    /// Transitions the gate to the paused state.
    /// Returns <see langword="true"/> if this call actually transitioned from unpaused to paused;
    /// <see langword="false"/> if already paused or disposed.
    /// </summary>
    public ValueTask<bool> PauseAsync()
    {
        lock (_lock)
        {
            if (_disposed || _paused)
            {
                return new ValueTask<bool>(result: false);
            }

            _paused = true;
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return new ValueTask<bool>(result: true);
    }

    /// <summary>
    /// Transitions the gate to the resumed state.
    /// Returns <see langword="true"/> if this call actually transitioned from paused to resumed;
    /// <see langword="false"/> if already resumed or disposed.
    /// </summary>
    public ValueTask<bool> ResumeAsync()
    {
        TaskCompletionSource gateToComplete;
        lock (_lock)
        {
            if (_disposed || !_paused)
            {
                return new ValueTask<bool>(result: false);
            }

            _paused = false;
            gateToComplete = _gate;
        }

        gateToComplete.TrySetResult();
        return new ValueTask<bool>(result: true);
    }

    /// <summary>
    /// Opens the gate for good: every waiter resumes, and later <see cref="PauseAsync"/> calls do nothing.
    /// </summary>
    public void Release()
    {
        TaskCompletionSource gateToComplete;
        lock (_lock)
        {
            _disposed = true;
            _paused = false;
            gateToComplete = _gate;
        }

        gateToComplete.TrySetResult();
    }

    private static TaskCompletionSource _CreateCompletedGate()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult();
        return tcs;
    }
}
