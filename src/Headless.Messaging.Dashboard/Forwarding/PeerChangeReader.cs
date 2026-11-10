// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text.Json;
using Headless.Dashboard;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Relays another node's changes to this node's streams while a stream watches one of that node's topics, such as
/// <c>messaging.received@node-b</c>: it reads the node's change feed about once a second and republishes each change
/// with the node in its target.
/// </summary>
/// <remarks>
/// <para>
/// One reader runs per watched node, and it stops when the last stream watching the node closes. A node that does not
/// answer, or leaves discovery, is reported stale on the first failed read, and only that node's views show it. A
/// node that left discovery loses its cursor, so it starts over when it comes back.
/// </para>
/// <para>
/// A cursor the node no longer knows, such as after it restarted, is answered with <c>410</c>; the reader then starts
/// a new cursor and tells the node's views to refetch, because the changes in between were missed.
/// </para>
/// </remarks>
internal sealed class PeerChangeReader(
    MessagingForwarding forwarding,
    IDashboardEventPublisher publisher,
    TimeProvider timeProvider,
    ILogger<PeerChangeReader> logger
) : IDisposable
{
    /// <summary>How often a watched node's change feed is read.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    // A failing node is read less often, but never less than every few seconds, so a node that comes back is shown
    // fresh again soon.
    private const int _MaxSkippedTicks = 3;

    private readonly Dictionary<string, PeerWatch> _watches = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <summary>Starts relaying <paramref name="node"/>, or counts one more watcher of it.</summary>
    public void Watch(string node)
    {
        if (!forwarding.IsEnabled || forwarding.Directory.IsLocal(node))
        {
            return;
        }

        lock (_lock)
        {
            if (_watches.TryGetValue(node, out var watch))
            {
                watch.Count++;

                return;
            }

            watch = new PeerWatch();
            _watches[node] = watch;
            watch.Loop = Task.Run(() => _RunAsync(node, watch.Stop.Token));
        }
    }

    /// <summary>Counts one fewer watcher of <paramref name="node"/>, and stops relaying it after the last.</summary>
    public void Unwatch(string node)
    {
        lock (_lock)
        {
            if (!_watches.TryGetValue(node, out var watch) || --watch.Count > 0)
            {
                return;
            }

            _watches.Remove(node);
            watch.Stop.Cancel();
            watch.Stop.Dispose();
        }
    }

    /// <summary>Returns whether <paramref name="node"/> is relayed now.</summary>
    internal bool IsWatching(string node)
    {
        lock (_lock)
        {
            return _watches.ContainsKey(node);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var watch in _watches.Values)
            {
                watch.Stop.Cancel();
                watch.Stop.Dispose();
            }

            _watches.Clear();
        }
    }

    private async Task _RunAsync(string node, CancellationToken cancellationToken)
    {
        string? cursor = null;
        var lostFeed = false;
        var stale = false;
        var failures = 0;
        var skip = 0;

        try
        {
            using var timer = new PeriodicTimer(PollInterval, timeProvider);

            do
            {
                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                (string? Cursor, bool Ok, bool Reset) read;

                try
                {
                    read = await _ReadOnceAsync(node, cursor, cancellationToken).ConfigureAwait(false);
                }
                catch (JsonException)
                {
                    // A body this node cannot read counts as a failed read; the node is shown stale, not dropped.
                    read = (cursor, false, false);
                }

                var (next, ok, reset) = read;

                // A node gone from discovery loses its cursor: the feed it names may not come back as it was. The
                // feed it starts on its return begins at its latest change, so the views reset then, or they would
                // keep rows that changed while it was gone.
                if (cursor is not null && next is null && !reset)
                {
                    lostFeed = true;
                }
                else if (cursor is null && next is not null && lostFeed)
                {
                    reset = true;
                    lostFeed = false;
                }

                cursor = next;

                if (reset)
                {
                    publisher.PublishNodeReset(MessagingDashboardOptions.ModuleId, node);
                }

                if (ok == stale)
                {
                    stale = !ok;
                    publisher.PublishNodeStale(MessagingDashboardOptions.ModuleId, node, stale);
                }

                failures = ok ? 0 : failures + 1;
                skip = ok ? 0 : Math.Min((1 << Math.Min(failures, 4)) - 1, _MaxSkippedTicks);
            } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Unwatched: the loop ends with its last watcher.
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            // Unwatched while a read was starting.
        }
    }

    private async Task<(string? Cursor, bool Ok, bool Reset)> _ReadOnceAsync(
        string node,
        string? cursor,
        CancellationToken cancellationToken
    )
    {
        var peer = await forwarding.Directory.FindAsync(node, cancellationToken).ConfigureAwait(false);

        if (peer is null)
        {
            return (null, false, false);
        }

        if (cursor is null)
        {
            var start = await forwarding
                .Client.SendAsync(
                    peer,
                    HttpMethod.Get,
                    "/peer/changes/start",
                    user: null,
                    operate: false,
                    json: null,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (!start.IsSuccess)
            {
                return (null, false, false);
            }

            var started = JsonSerializer.Deserialize<PeerCursor>(start.Body, MessagingForwarding.JsonOptions);

            return (started?.Cursor, started is not null, false);
        }

        var response = await forwarding
            .Client.SendAsync(
                peer,
                HttpMethod.Get,
                "/peer/changes?cursor=" + Uri.EscapeDataString(cursor),
                user: null,
                operate: false,
                json: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (response.Answered && response.Status == (int)HttpStatusCode.Gone)
        {
            logger.PeerCursorUnknown(node);

            return (null, true, true);
        }

        if (!response.IsSuccess)
        {
            return (cursor, false, false);
        }

        var batch = JsonSerializer.Deserialize<DashboardChangeBatch>(response.Body, MessagingForwarding.JsonOptions);

        if (batch is null)
        {
            return (cursor, false, false);
        }

        foreach (var change in batch.Changes)
        {
            // The node's own changes name no node; the topic a stream watches here does.
            publisher.Publish(change with { Target = change.Target with { Node = node } });
        }

        return (batch.Cursor, true, false);
    }

    private sealed class PeerWatch
    {
        public int Count { get; set; } = 1;

        public CancellationTokenSource Stop { get; } = new();

        public Task? Loop { get; set; }
    }
}

/// <summary>A peer's change cursor.</summary>
internal sealed record PeerCursor(string Cursor);

internal static partial class PeerChangeReaderLog
{
    [LoggerMessage(
        EventId = 2153,
        EventName = "MessagingDashboardPeerCursorUnknown",
        Level = LogLevel.Information,
        Message = "Messaging node {Node} no longer knows the change cursor; its views are told to refetch"
    )]
    public static partial void PeerCursorUnknown(this ILogger logger, string node);
}
