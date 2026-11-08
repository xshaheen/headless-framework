// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Dashboard;

namespace Tests.Forwarding;

/// <summary>Records what a node's streams are told, and completes a wait when a matching event arrives.</summary>
internal sealed class RecordingEventPublisher : IDashboardEventPublisher
{
    private readonly ConcurrentQueue<StreamEvent> _events = new();

    private readonly ConcurrentQueue<(
        Func<StreamEvent, bool> Match,
        TaskCompletionSource<StreamEvent> Signal
    )> _waiters = new();

    public IReadOnlyList<StreamEvent> Events => [.. _events];

    public void Publish(DashboardChange change) => _Record(new StreamEvent("change", change.Target.Node, change));

    public void PublishNodeStale(string moduleId, string node, bool stale) =>
        _Record(new StreamEvent(stale ? "stale" : "fresh", node, Change: null));

    public void PublishNodeReset(string moduleId, string node) => _Record(new StreamEvent("reset", node, Change: null));

    /// <summary>Completes with the next event that <paramref name="match"/> accepts, published after this call.</summary>
    public Task<StreamEvent> NextAsync(Func<StreamEvent, bool> match)
    {
        var signal = new TaskCompletionSource<StreamEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters.Enqueue((match, signal));

        return signal.Task;
    }

    private void _Record(StreamEvent streamEvent)
    {
        _events.Enqueue(streamEvent);

        foreach (var (match, signal) in _waiters)
        {
            if (!signal.Task.IsCompleted && match(streamEvent))
            {
                signal.TrySetResult(streamEvent);
            }
        }
    }

    public sealed record StreamEvent(string Kind, string? Node, DashboardChange? Change);
}
