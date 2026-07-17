// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Tests;

/// <summary>An <see cref="IServiceProvider" /> that resolves nothing — for coordinator paths that never touch DI.</summary>
public sealed class EmptyServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        return null;
    }
}

/// <summary>
/// A single-threaded <see cref="SynchronizationContext" /> for deadlock-probing sync-over-async paths: it pumps
/// posted continuations on one dedicated thread, so a body that blocks waiting on a continuation it posted here
/// deadlocks deterministically (surfaced as <see cref="Run" /> returning <see langword="false" /> on timeout).
/// </summary>
public sealed class SingleThreadSynchronizationContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Add((d, state));
    }

    public static bool Run(Action action, TimeSpan timeout)
    {
        using var completed = new ManualResetEventSlim(false);
        var context = new SingleThreadSynchronizationContext();

        var thread = new Thread(() =>
        {
            SetSynchronizationContext(context);

            try
            {
                action();
            }
            finally
            {
                completed.Set();
                context._queue.CompleteAdding();
            }

            foreach (var (callback, state) in context._queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        })
        {
            IsBackground = true,
        };

        thread.Start();

        return completed.Wait(timeout);
    }
}
