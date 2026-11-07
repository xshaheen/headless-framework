// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Dashboard;

/// <summary>
/// The single way into the node's event ring: drops a change whose version was already sent for its object, then
/// coalesces the changes to one object within <see cref="CoalesceWindow"/> into the latest before appending them.
/// Module publishes and the change readers both go through it, so a change both report reaches the streams once.
/// </summary>
internal sealed class DashboardEventPublisher(EventRingBuffer buffer, TimeProvider timeProvider)
    : IDashboardEventPublisher,
        IDisposable
{
    /// <summary>How long changes wait to be merged with later changes to the same object.</summary>
    internal static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The number of objects whose last version is remembered. A busy store changes far more objects than streams can
    /// show; an object forgotten past this cap only risks one duplicate event, which views patch idempotently.
    /// </summary>
    internal const int MaxTrackedObjects = 10_000;

    private readonly Lock _lock = new();
    private readonly Dictionary<ObjectKey, string> _versions = [];
    private readonly Queue<ObjectKey> _versionOrder = new();
    private readonly OrderedDictionary<PendingKey, PendingEvent> _pending = [];
    private readonly HashSet<string> _staleModules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _unavailable = new(StringComparer.Ordinal);
    private readonly HashSet<NodeKey> _staleNodes = [];
    private readonly EventRingBuffer _buffer = Argument.IsNotNull(buffer);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);
    private ITimer? _flushTimer;
    private bool _disposed;

    /// <inheritdoc />
    public void Publish(DashboardChange change)
    {
        Argument.IsNotNull(change);
        Argument.IsNotNull(change.Target);
        Argument.IsNotNull(change.Version);

        Accept(change);
    }

    /// <inheritdoc />
    public void PublishNodeStale(string moduleId, string node, bool stale)
    {
        Argument.IsNotNullOrEmpty(moduleId);
        Argument.IsNotNullOrEmpty(node);

        var key = new NodeKey(moduleId, node);

        // Recorded before the event is appended, as a module's own stale state is, so a stream opening meanwhile
        // either finds the node stale or receives the event.
        lock (_lock)
        {
            if (stale ? !_staleNodes.Add(key) : !_staleNodes.Remove(key))
            {
                return;
            }
        }

        _buffer.Append(StreamEventType.NodeStale, moduleId, stale: stale, node: node);
    }

    /// <inheritdoc />
    public void PublishNodeReset(string moduleId, string node)
    {
        Argument.IsNotNullOrEmpty(moduleId);
        Argument.IsNotNullOrEmpty(node);

        _buffer.Append(StreamEventType.NodeReset, moduleId, node: node);
    }

    /// <summary>
    /// Accepts a change from a module or a change reader: queues it for the next flush when its version is newer than
    /// the last one accepted for its object, and drops it otherwise.
    /// </summary>
    internal void Accept(DashboardChange change)
    {
        // A module's reader hands over what the module returned; a change without a target or version cannot be
        // deduplicated or routed, so it is dropped rather than failing the whole read.
        if (change.Target is not { } target || change.Version is null)
        {
            return;
        }

        var key = new ObjectKey(target.Module, target.Kind, target.Id, target.Node);

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (_versions.TryGetValue(key, out var accepted) && CompareVersions(change.Version, accepted) <= 0)
            {
                return;
            }

            _Remember(key, change.Version);
            _Queue(new PendingKey(StreamEventType.Change, key), new PendingEvent(target.Module, change));

            if (change.AffectsAttention)
            {
                var attention = new PendingKey(
                    StreamEventType.AttentionChanged,
                    new ObjectKey(target.Module, string.Empty, string.Empty, Node: null)
                );
                _Queue(attention, new PendingEvent(target.Module, Change: null));
            }
        }
    }

    /// <summary>
    /// Reports at once that a module's change reads started or stopped failing, so its live views show or clear
    /// <c>Stale</c>.
    /// </summary>
    /// <remarks>
    /// The module's state is recorded before the event is appended, so a stream that reads the ring's head and then
    /// <see cref="Health"/> either finds the module stale or receives the event.
    /// </remarks>
    internal void ReportStale(string moduleId, bool stale)
    {
        string[]? unavailable;

        lock (_lock)
        {
            if (stale)
            {
                _staleModules.Add(moduleId);
            }
            else
            {
                _staleModules.Remove(moduleId);
            }

            unavailable = _unavailable.GetValueOrDefault(moduleId);
        }

        _buffer.Append(StreamEventType.ModuleStale, moduleId, stale: stale, unavailable: unavailable);
    }

    /// <summary>
    /// Reports the sources a module's last change read could not reach, so its live views name them while the module
    /// stays live. Only a change from the last report reaches the streams.
    /// </summary>
    internal void ReportUnavailable(string moduleId, IReadOnlyList<string> sources)
    {
        bool stale;
        string[]? unavailable;

        lock (_lock)
        {
            var current = _unavailable.GetValueOrDefault(moduleId) ?? [];

            if (current.SequenceEqual(sources, StringComparer.Ordinal))
            {
                return;
            }

            unavailable = sources.Count == 0 ? null : [.. sources];

            if (unavailable is null)
            {
                _unavailable.Remove(moduleId);
            }
            else
            {
                _unavailable[moduleId] = unavailable;
            }

            stale = _staleModules.Contains(moduleId);
        }

        _buffer.Append(StreamEventType.ModuleStale, moduleId, stale: stale, unavailable: unavailable);
    }

    /// <summary>
    /// Forgets that a module was stale when its reader stops: no stream is open then, and the next reader reports the
    /// store's state afresh.
    /// </summary>
    internal void ForgetStale(string moduleId)
    {
        lock (_lock)
        {
            _staleModules.Remove(moduleId);
            _unavailable.Remove(moduleId);
        }
    }

    /// <summary>
    /// Forgets that a node was stale when no stream watches it any more; the module reports the node afresh when one
    /// does again.
    /// </summary>
    internal void ForgetNode(string moduleId, string node)
    {
        lock (_lock)
        {
            _staleNodes.Remove(new NodeKey(moduleId, node));
        }
    }

    /// <summary>Returns whether the module's change reads are marked as failing.</summary>
    internal bool IsStale(string moduleId)
    {
        lock (_lock)
        {
            return _staleModules.Contains(moduleId);
        }
    }

    /// <summary>
    /// Returns the modules whose change reads are failing or miss some sources, and the stale nodes, so a stream that
    /// opens later shows them.
    /// </summary>
    internal DashboardStreamHealth Health()
    {
        lock (_lock)
        {
            var modules = new List<ModuleHealth>();

            foreach (var moduleId in _staleModules.Union(_unavailable.Keys, StringComparer.Ordinal))
            {
                modules.Add(
                    new ModuleHealth(
                        moduleId,
                        _staleModules.Contains(moduleId),
                        _unavailable.GetValueOrDefault(moduleId)
                    )
                );
            }

            return new DashboardStreamHealth(modules, [.. _staleNodes.Select(static n => (n.Module, n.Node))]);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _flushTimer?.Dispose();
            _flushTimer = null;
            _pending.Clear();
        }
    }

    /// <summary>
    /// Orders two versions of one object: as integers when both parse as one, so a sequence that gains a digit stays
    /// ordered, and ordinally otherwise, which orders fixed-width timestamps.
    /// </summary>
    internal static int CompareVersions(string left, string right)
    {
        if (
            long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber)
            && long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber)
        )
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.CompareOrdinal(left, right);
    }

    private void _Remember(ObjectKey key, string version)
    {
        if (!_versions.ContainsKey(key))
        {
            _versionOrder.Enqueue(key);

            if (_versionOrder.Count > MaxTrackedObjects)
            {
                _versions.Remove(_versionOrder.Dequeue());
            }
        }

        _versions[key] = version;
    }

    // A later change to the same object replaces the queued one in place, so the flush keeps arrival order.
    private void _Queue(PendingKey key, PendingEvent pending)
    {
        _pending[key] = pending;
        _flushTimer ??= _timeProvider.CreateTimer(
            static state => ((DashboardEventPublisher)state!)._Flush(),
            this,
            CoalesceWindow,
            Timeout.InfiniteTimeSpan
        );
    }

    private void _Flush()
    {
        KeyValuePair<PendingKey, PendingEvent>[] pending;

        lock (_lock)
        {
            pending = [.. _pending];
            _pending.Clear();
            _flushTimer?.Dispose();
            _flushTimer = null;
        }

        foreach (var (key, value) in pending)
        {
            _buffer.Append(key.Type, value.ModuleId, value.Change);
        }
    }

    private readonly record struct ObjectKey(string Module, string Kind, string Id, string? Node);

    private readonly record struct NodeKey(string Module, string Node);

    private readonly record struct PendingKey(StreamEventType Type, ObjectKey Object);

    private sealed record PendingEvent(string ModuleId, DashboardChange? Change);
}

/// <summary>What a stream that opens now must be told before the ring's events: the modules and nodes not live.</summary>
/// <param name="Modules">The modules whose change reads are failing or miss some sources.</param>
/// <param name="StaleNodes">The nodes their modules reported stale.</param>
internal sealed record DashboardStreamHealth(
    IReadOnlyList<ModuleHealth> Modules,
    IReadOnlyList<(string Module, string Node)> StaleNodes
);

/// <summary>One module's change-read state.</summary>
/// <param name="ModuleId">The module.</param>
/// <param name="Stale">Whether its change reads are failing.</param>
/// <param name="Unavailable">The sources its last read could not reach, or <see langword="null"/>.</param>
internal sealed record ModuleHealth(string ModuleId, bool Stale, IReadOnlyList<string>? Unavailable);
