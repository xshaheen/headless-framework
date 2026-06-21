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
[PublicAPI]
public sealed class PollingReleaseSignal(TimeProvider? timeProvider = null) : IReleaseSignal
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _signals = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

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
        var signal = _signals.GetOrAdd(
            resource,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        );

        var delay = _timeProvider.Delay(pollingFallback, cancellationToken);
        var completed = await Task.WhenAny(signal.Task, delay).ConfigureAwait(false);

        if (completed == signal.Task)
        {
            // Remove only if this is still our own signal. A blind TryRemove(resource) could drop a fresh TCS
            // that a concurrent waiter registered under the same key after our publisher already removed ours,
            // silently demoting that waiter's push wake-up to the polling fallback.
            ((ICollection<KeyValuePair<string, TaskCompletionSource>>)_signals).Remove(
                new KeyValuePair<string, TaskCompletionSource>(resource, signal)
            );
        }

        await completed.ConfigureAwait(false);
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
            signal.TrySetResult();
        }

        return ValueTask.CompletedTask;
    }
}
