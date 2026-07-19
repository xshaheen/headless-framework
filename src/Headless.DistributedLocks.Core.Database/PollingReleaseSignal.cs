// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Default in-process <see cref="IReleaseSignal"/>. Wakes a local waiter through a per-resource
/// <see cref="TaskCompletionSource"/> and otherwise relies on the caller-supplied polling fallback,
/// so correctness never depends on the push signal being delivered. A provider with a native
/// cross-process push channel (for example <c>LISTEN/NOTIFY</c>) can substitute its own
/// implementation; this one is correct but only signals waiters in the same process.
/// </summary>
/// <param name="timeProvider">Clock used for the polling delay; defaults to <see cref="TimeProvider.System"/>.</param>
internal sealed class PollingReleaseSignal(TimeProvider? timeProvider = null) : IReleaseSignal
{
    private readonly ConcurrentDictionary<string, SignalEntry> _signals = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    internal int ActiveResourceCount => _signals.Count;

    /// <summary>
    /// Waits until either the resource is signalled via <see cref="PublishAsync"/> or the polling
    /// fallback elapses, whichever happens first. Returning on the fallback is expected: the caller
    /// re-probes the underlying store, so a missed or coalesced signal only costs one extra poll.
    /// </summary>
    /// <param name="resource">Resource key whose release the caller is waiting for.</param>
    /// <param name="pollingFallback">Maximum time to wait before returning to let the caller re-probe.</param>
    /// <param name="cancellationToken">Token used to cancel the wait.</param>
    public async ValueTask WaitAsync(
        string resource,
        TimeSpan pollingFallback,
        CancellationToken cancellationToken = default
    )
    {
        var signal = _Register(resource);

        try
        {
            using var delayCancellation = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new CancellationTokenSource();
            var delay = _timeProvider.Delay(pollingFallback, delayCancellation.Token);
            var completed = await Task.WhenAny(signal.Task, delay).ConfigureAwait(false);

            if (completed == signal.Task)
            {
                await delayCancellation.CancelAsync().ConfigureAwait(false);

                try
                {
                    await delay.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (delayCancellation.IsCancellationRequested)
                {
                    // The signal won. Drain the cancelled fallback so its timer registration cannot outlive this wait.
                }
            }
            else
            {
                await delay.ConfigureAwait(false);
            }
        }
        finally
        {
            if (signal.Release())
            {
                _Remove(resource, signal);
            }
        }
    }

    /// <summary>
    /// Wakes the waiter (if any) currently registered for <paramref name="resource"/>. Best-effort:
    /// a publish that races ahead of a waiter's registration is simply absorbed by the polling
    /// fallback rather than tracked, so this never blocks and never throws on a missing waiter.
    /// </summary>
    /// <param name="resource">Resource key whose waiter should be woken.</param>
    /// <param name="cancellationToken">Token observed before publishing.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask PublishAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_signals.TryRemove(resource, out var signal))
        {
            signal.RetireAndSignal();
        }

        return ValueTask.CompletedTask;
    }

    private SignalEntry _Register(string resource)
    {
        while (true)
        {
            var signal = _signals.GetOrAdd(resource, static _ => new SignalEntry());

            if (signal.TryRegister())
            {
                return signal;
            }

            // The entry retired between lookup and registration. Remove only that exact instance; a publisher
            // may already have installed a fresh entry for later waiters under the same resource key.
            _Remove(resource, signal);
        }
    }

    private void _Remove(string resource, SignalEntry signal)
    {
        _signals.TryRemove(KeyValuePair.Create(resource, signal));
    }

    private sealed class SignalEntry
    {
        private const int _RetiredMask = int.MinValue;
        private const int _WaiterCountMask = int.MaxValue;
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _state;

        public Task Task => _completion.Task;

        public bool TryRegister()
        {
            while (true)
            {
                var state = Volatile.Read(ref _state);

                if ((state & _RetiredMask) != 0)
                {
                    return false;
                }

                var waiterCount = state & _WaiterCountMask;

                if (waiterCount == _WaiterCountMask)
                {
                    throw new InvalidOperationException("The release signal has too many concurrent waiters.");
                }

                if (Interlocked.CompareExchange(ref _state, state + 1, state) == state)
                {
                    return true;
                }
            }
        }

        public bool Release()
        {
            while (true)
            {
                var state = Volatile.Read(ref _state);
                var waiterCount = state & _WaiterCountMask;

                if (waiterCount == 0)
                {
                    throw new InvalidOperationException("The release signal has no registered waiter.");
                }

                var updated = (state & _RetiredMask) | (waiterCount - 1);

                if (Interlocked.CompareExchange(ref _state, updated, state) != state)
                {
                    continue;
                }

                if (updated != 0)
                {
                    return false;
                }

                // Registration may win between the decrement and retirement. In that case the new waiter owns
                // the live entry and will attempt retirement when it departs.
                return Interlocked.CompareExchange(ref _state, _RetiredMask, 0) == 0;
            }
        }

        public void RetireAndSignal()
        {
            while (true)
            {
                var state = Volatile.Read(ref _state);

                if ((state & _RetiredMask) != 0)
                {
                    break;
                }

                if (Interlocked.CompareExchange(ref _state, state | _RetiredMask, state) == state)
                {
                    break;
                }
            }

            _completion.TrySetResult();
        }
    }
}
