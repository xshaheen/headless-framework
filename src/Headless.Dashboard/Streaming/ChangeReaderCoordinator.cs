// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// Runs one change reader per module while the node has at least one live stream open: the node's first stream starts
/// them and its last close stops them, so a node nobody watches reads nothing.
/// </summary>
/// <remarks>
/// <para>
/// Readers read every change of their module, whatever the open streams subscribe to, and each stream filters. A
/// module refused for its SDK version gets no reader. A restart after a stop records a gap in the event ring, because
/// the store changes made while the readers were stopped never reached it.
/// </para>
/// <para>
/// It also counts the streams subscribed to each node topic, <c>{module}.{kind}@{node}</c>, and tells a module that
/// implements <see cref="IDashboardNodeTopicObserver"/> when the first stream subscribes to one of its node topics and
/// when the last one closes. Both calls run under the coordinator's lock, so the calls for one topic never reorder.
/// </para>
/// </remarks>
internal sealed class ChangeReaderCoordinator(
    DashboardManifestProvider manifest,
    DashboardEventPublisher publisher,
    EventRingBuffer buffer,
    IOptions<HeadlessDashboardOptions> options,
    TimeProvider timeProvider,
    ILogger<ChangeReader> logger
) : IDisposable
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, WatchedTopic> _nodeTopics = new(StringComparer.Ordinal);
    private ChangeReader[] _readers = [];
    private int _openStreams;
    private bool _disposed;

    /// <summary>
    /// Counts one more open stream, starting the readers when it is the first, and counts its node topics. Dispose the
    /// returned lease when the stream closes; disposing it again does nothing.
    /// </summary>
    /// <param name="topics">The stream's subscribed topics, already limited to modules the user may view.</param>
    /// <exception cref="InvalidOperationException">A module's manifest is missing or invalid.</exception>
    public IDisposable Open(IReadOnlyList<string> topics)
    {
        string[] watched = [];

        lock (_lock)
        {
            if (!_disposed && _openStreams++ == 0)
            {
                try
                {
                    _Start();
                }
                catch
                {
                    // The stream fails to open, so it is not counted; the next stream tries the start again.
                    _openStreams--;
                    throw;
                }
            }

            if (!_disposed)
            {
                watched = _Watch(topics);
            }
        }

        return new Lease(this, watched);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _Stop();

            // Every watched topic is unwatched once: here, or when its last stream closes, whichever comes first.
            foreach (var topic in _nodeTopics.Values)
            {
                _Notify(topic, watched: false);
            }

            _nodeTopics.Clear();
        }
    }

    /// <summary>
    /// Splits <c>{module}.{kind}@{node}</c> into its parts; returns <see langword="null"/> for a topic without a node.
    /// </summary>
    internal static DashboardNodeTopic? ParseNodeTopic(string topic)
    {
        var dot = topic.IndexOf('.', StringComparison.Ordinal);
        var at = topic.IndexOf('@', StringComparison.Ordinal);

        if (dot <= 0 || at <= dot + 1 || at == topic.Length - 1)
        {
            return null;
        }

        return new DashboardNodeTopic
        {
            Module = topic[..dot],
            Kind = topic[(dot + 1)..at],
            Node = topic[(at + 1)..],
        };
    }

    private void _Close(string[] watched)
    {
        lock (_lock)
        {
            _Unwatch(watched);

            if (_disposed || --_openStreams > 0)
            {
                return;
            }

            _openStreams = 0;
            _Stop();
        }
    }

    // Counts the stream's node topics, and returns the ones it counted, which its lease uncounts.
    private string[] _Watch(IReadOnlyList<string> topics)
    {
        var watched = new List<string>();

        foreach (var topic in topics)
        {
            if (!_nodeTopics.TryGetValue(topic, out var entry))
            {
                if (ParseNodeTopic(topic) is not { } parsed)
                {
                    continue;
                }

                entry = new WatchedTopic(parsed, _FindObserver(parsed.Module));
                _nodeTopics[topic] = entry;
                _Notify(entry, watched: true);
            }

            entry.Streams++;
            watched.Add(topic);
        }

        return [.. watched];
    }

    private void _Unwatch(string[] watched)
    {
        foreach (var topic in watched)
        {
            // Absent once the coordinator was disposed, which already unwatched it.
            if (!_nodeTopics.TryGetValue(topic, out var entry) || --entry.Streams > 0)
            {
                continue;
            }

            _nodeTopics.Remove(topic);
            _Notify(entry, watched: false);

            // A node no stream watches any more keeps no stale state; the module reports it afresh on the next watch.
            var node = entry.Topic;
            var stillWatched = _nodeTopics.Values.Any(other =>
                string.Equals(other.Topic.Module, node.Module, StringComparison.Ordinal)
                && string.Equals(other.Topic.Node, node.Node, StringComparison.Ordinal)
            );

            if (!stillWatched)
            {
                publisher.ForgetNode(node.Module, node.Node);
            }
        }
    }

    private IDashboardNodeTopicObserver? _FindObserver(string moduleId)
    {
        var module = manifest.Manifest.Modules.FirstOrDefault(m =>
            string.Equals(m.Id, moduleId, StringComparison.Ordinal)
        );

        return module is { IsRefused: false, Module: IDashboardNodeTopicObserver observer } ? observer : null;
    }

    private void _Notify(WatchedTopic entry, bool watched)
    {
        if (entry.Observer is not { } observer)
        {
            return;
        }

        try
        {
            if (watched)
            {
                observer.OnNodeTopicWatched(entry.Topic);
            }
            else
            {
                observer.OnNodeTopicUnwatched(entry.Topic);
            }
        }
        catch (Exception e)
        {
            // The module's callback is this boundary's only call: a failing module must not fail the stream that
            // opened or closed, so the failure is logged and the stream goes on.
            logger.NodeTopicCallbackFailed(e, entry.Topic.Module, entry.Topic.Topic);
        }
    }

    private void _Start()
    {
        var readers = new List<ChangeReader>();

        foreach (var module in manifest.Manifest.Modules)
        {
            if (module.IsRefused || module.Module is not IDashboardChangeSource source)
            {
                continue;
            }

            readers.Add(
                new ChangeReader(module.Id, source, publisher, options.Value.ChangePollInterval, timeProvider, logger)
            );
        }

        // Only a store read can miss a change while stopped; a module's own publishes always reach the ring.
        if (readers.Count > 0)
        {
            buffer.MarkGap();
        }

        _readers = [.. readers];
    }

    private void _Stop()
    {
        foreach (var reader in _readers)
        {
            reader.Dispose();
        }

        _readers = [];
    }

    private sealed class Lease(ChangeReaderCoordinator coordinator, string[] watched) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                coordinator._Close(watched);
            }
        }
    }

    private sealed class WatchedTopic(DashboardNodeTopic topic, IDashboardNodeTopicObserver? observer)
    {
        public DashboardNodeTopic Topic { get; } = topic;

        public IDashboardNodeTopicObserver? Observer { get; } = observer;

        public int Streams { get; set; }
    }
}

internal static partial class ChangeReaderCoordinatorLog
{
    [LoggerMessage(
        EventId = 2212,
        EventName = "DashboardNodeTopicCallbackFailed",
        Level = LogLevel.Warning,
        Message = "Dashboard module {ModuleId} failed to handle a change in who watches {Topic}; the stream goes on"
    )]
    internal static partial void NodeTopicCallbackFailed(
        this ILogger logger,
        Exception exception,
        string moduleId,
        string topic
    );
}
