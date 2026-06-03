// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public sealed class PollingReleaseSignal(TimeProvider? timeProvider = null) : IReleaseSignal
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _signals = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

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
            _signals.TryRemove(resource, out _);
        }

        await completed.ConfigureAwait(false);
    }

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
