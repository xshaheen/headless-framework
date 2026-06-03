// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Nito.AsyncEx;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Default in-process <see cref="IReleaseSignal"/>. Wakes local waiters via a per-resource
/// <see cref="Nito.AsyncEx.AsyncAutoResetEvent"/> and otherwise relies on the caller-supplied polling
/// fallback. A provider with a native cross-process push channel (for example <c>LISTEN/NOTIFY</c>) can
/// substitute its own implementation; this one is correct but only signals waiters in the same process.
/// </summary>
/// <param name="timeProvider">Clock used for the polling delay; defaults to <see cref="TimeProvider.System"/>.</param>
[PublicAPI]
public sealed class PollingReleaseSignal(TimeProvider? timeProvider = null) : IReleaseSignal
{
    private readonly ConcurrentDictionary<string, Waiters> _signals = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask WaitAsync(
        string resource,
        TimeSpan pollingFallback,
        CancellationToken cancellationToken = default
    )
    {
        // AsyncAutoResetEvent delivers each Set() to exactly one waiter and latches a single pending
        // signal, so a publish that races ahead of a late waiter's registration is not missed.
        var waiters = _signals.AddOrUpdate(
            resource,
            static _ => new Waiters(),
            static (_, existing) =>
            {
                existing.Increment();
                return existing;
            }
        );

        // Cancel the polling delay as soon as the push signal wins, so the timer task is not leaked.
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var signalTask = waiters.Event.WaitAsync(cancellationToken);
            var delayTask = _timeProvider.Delay(pollingFallback, delayCts.Token);

            var completed = await Task.WhenAny(signalTask, delayTask).ConfigureAwait(false);

            if (completed == signalTask)
            {
                await delayCts.CancelAsync().ConfigureAwait(false);
            }

            await completed.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (delayCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The delay was cancelled because the push signal won; not an error.
        }
        finally
        {
            _Release(resource, waiters);
        }
    }

    public ValueTask PublishAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_signals.TryGetValue(resource, out var waiters))
        {
            waiters.Event.Set();
        }

        return ValueTask.CompletedTask;
    }

    private void _Release(string resource, Waiters waiters)
    {
        if (waiters.Decrement() == 0)
        {
            // Only remove if this is still the current entry and nobody else re-incremented it.
            _signals.TryRemove(new KeyValuePair<string, Waiters>(resource, waiters));
        }
    }

    private sealed class Waiters
    {
        // Seeded at 1 for the first waiter (the AddOrUpdate add delegate); each additional waiter
        // increments via the update delegate. Every waiter decrements exactly once in its finally.
        private int _refCount = 1;

        public AsyncAutoResetEvent Event { get; } = new(set: false);

        public void Increment() => Interlocked.Increment(ref _refCount);

        public int Decrement() => Interlocked.Decrement(ref _refCount);
    }
}
