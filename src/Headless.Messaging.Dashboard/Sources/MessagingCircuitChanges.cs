// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Publishes each change of this node's circuit breakers to its own dashboard streams, on the topic
/// <c>messaging.circuit</c>, and keeps what another node needs to relay them: every circuit's latest version.
/// </summary>
/// <remarks>
/// <para>
/// A circuit lives in this node's memory, so no store carries its changes to another node. The breaker reports each
/// change here; this node's streams get it at once, and a node that relays this one reads <see cref="ReadSince"/>
/// with the position its last read returned.
/// </para>
/// <para>
/// A version is the change's time in UTC ticks, raised when needed so it is always later than the one before. It stays
/// ordered across a restart, when every circuit starts closed again: a position from before this process started is
/// answered with every circuit, so a relaying node's views drop what the earlier process left open.
/// </para>
/// </remarks>
internal sealed class MessagingCircuitChanges : ICircuitStateObserver
{
    private readonly string _nodeName;
    private readonly IDashboardEventPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly Lazy<ICircuitBreakerMonitor?> _monitor;
    private readonly Dictionary<string, long> _versions = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private readonly long _startedAt;
    private long _last;

    public MessagingCircuitChanges(
        string nodeName,
        IDashboardEventPublisher publisher,
        TimeProvider timeProvider,
        IServiceProvider services
    )
    {
        Argument.IsNotNull(services);

        _nodeName = Argument.IsNotNull(nodeName);
        _publisher = Argument.IsNotNull(publisher);
        _timeProvider = Argument.IsNotNull(timeProvider);
        // Resolved on the first change: the breaker reports here, so resolving it now would loop.
        _monitor = new Lazy<ICircuitBreakerMonitor?>(services.GetService<ICircuitBreakerMonitor>);
        _startedAt = _last = _timeProvider.GetUtcNow().UtcTicks;
    }

    /// <summary>The position after the latest change, which a relaying node's first read starts from.</summary>
    public long Position
    {
        get
        {
            lock (_lock)
            {
                return _last;
            }
        }
    }

    /// <summary>
    /// The version of the circuit's latest change, or of this process's start for one that has not changed since.
    /// </summary>
    public string VersionOf(string circuitKey)
    {
        lock (_lock)
        {
            return MessagingViews.VersionOf(_versions.GetValueOrDefault(circuitKey, _startedAt));
        }
    }

    public void OnCircuitChanged(string circuitKey)
    {
        long version;

        // The version is taken before the circuit is read, so the latest version always carries a read at least as
        // late as every earlier change, and the stream may drop the earlier ones.
        lock (_lock)
        {
            version = _last = Math.Max(_timeProvider.GetUtcNow().UtcTicks, _last + 1);
            _versions[circuitKey] = version;
        }

        _publisher.Publish(_Change(circuitKey, version));
    }

    /// <summary>
    /// Returns the circuits that changed after <paramref name="position"/>, each as it is now, and the position to
    /// read from next.
    /// </summary>
    public (IReadOnlyList<DashboardChange> Changes, long Position) ReadSince(long position)
    {
        List<(string Key, long Version)> changed;
        long next;

        lock (_lock)
        {
            next = Math.Max(position, _last);

            if (position < _startedAt)
            {
                // A position another process issued: its circuits are gone, so every circuit is sent as it is now.
                changed =
                [
                    .. (_monitor.Value?.KnownConsumers ?? (IEnumerable<string>)[])
                        .Union(_versions.Keys, StringComparer.Ordinal)
                        .Select(key => (key, _versions.GetValueOrDefault(key, _startedAt))),
                ];
            }
            else
            {
                changed = [.. _versions.Where(entry => entry.Value > position).Select(e => (e.Key, e.Value))];
            }
        }

        return ([.. changed.Select(entry => _Change(entry.Key, entry.Version))], next);
    }

    private DashboardChange _Change(string circuitKey, long version)
    {
        return MessagingViews.CircuitChange(
            _nodeName,
            circuitKey,
            _monitor.Value?.GetSnapshot(circuitKey),
            MessagingViews.VersionOf(version)
        );
    }
}
