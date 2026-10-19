// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Helpers;

/// <summary>
/// Wakes waiters each time a recorder changes its state, so a test waits on a condition instead of polling it. The
/// owner calls <see cref="Notify"/> after every update has been applied.
/// </summary>
[PublicAPI]
public sealed class ChangeSignal
{
    private readonly Lock _lock = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Wakes every current waiter; a waiter that arrives later waits for the next change.</summary>
    public void Notify()
    {
        TaskCompletionSource changed;

        lock (_lock)
        {
            changed = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        changed.TrySetResult();
    }

    /// <summary>
    /// Waits until <paramref name="condition"/> holds, re-checking after every <see cref="Notify"/>. Returns
    /// <see langword="false"/> when <paramref name="timeout"/> passes first, so a test can also assert that nothing more
    /// happened within a window; cancelling <paramref name="cancellationToken"/> throws instead.
    /// </summary>
    public async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = timeout.ToCancellationTokenSource(cancellationToken);

        try
        {
            while (true)
            {
                // Taken before the check, so a change that lands between the check and the wait still wakes it.
                Task changed;

                lock (_lock)
                {
                    changed = _changed.Task;
                }

                if (condition())
                {
                    return true;
                }

                await changed.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
