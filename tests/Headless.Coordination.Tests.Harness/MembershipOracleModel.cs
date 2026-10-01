// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Coordination;

namespace Tests;

/// <summary>One side of a differential run: the model or a real store, executing a history one operation at a time.</summary>
public interface IMembershipOracleTarget : IAsyncDisposable
{
    /// <summary>Runs <paramref name="op" /> and returns what it observed: the call's outcome and the stored rows after it.</summary>
    Task<string> ExecuteAsync(MembershipOracleOp op, CancellationToken cancellationToken);
}

/// <summary>What both sides of a run share: the node pool, the descriptor variants, and how a slot names an incarnation.</summary>
public sealed class MembershipOracleScope(MembershipOracleHistory history)
{
    /// <summary>Registration content: a descriptor is write-once, so registering again with another variant shows it.</summary>
    internal static readonly (string? Role, Dictionary<string, string> Metadata)[] Descriptors =
    [
        (null, new Dictionary<string, string>(StringComparer.Ordinal)),
        ("worker", new Dictionary<string, string>(StringComparer.Ordinal) { ["zone"] = "a" }),
        ("Worker", new Dictionary<string, string>(StringComparer.Ordinal) { ["zone"] = "b", ["rack"] = "7" }),
    ];

    private readonly List<long>[] _allocated = [.. history.Nodes.Select(static _ => new List<long>())];

    public MembershipOracleHistory History { get; } = history;

    public void Allocated(int node, long incarnation) => _allocated[node].Add(incarnation);

    public NodeIdentity Identity(int node, int slot)
    {
        var seen = _allocated[node];
        var latest = seen.Count == 0 ? 1 : seen[^1];
        var incarnation = slot switch
        {
            0 => latest,
            1 => seen.Count >= 2 ? seen[^2] : latest,
            _ => latest + 1,
        };

        return new NodeIdentity(new NodeId(History.Nodes[node]), new NodeIncarnation(incarnation));
    }

    /// <summary>Names a node by its index in the history, so a report reads <c>n0</c> rather than escaped text.</summary>
    public string Name(string nodeId)
    {
        var index = History.Nodes.ToList().IndexOf(nodeId);

        return index < 0 ? "?" + MembershipOracleGenerator.Escape(nodeId) : $"n{index}";
    }

    public string Describe(NodeIdentity identity)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Name(identity.NodeId.Value)}@{identity.Incarnation.Value}"
        );
    }

    public string Describe(IEnumerable<NodeLivenessSnapshot> snapshots)
    {
        // The store's own order is part of the contract, so it is kept rather than re-sorted here.
        return string.Join(
            ' ',
            snapshots.Select(s =>
            {
                var metadata = string.Join(
                    ',',
                    s.Metadata.OrderBy(static p => p.Key, StringComparer.Ordinal)
                        .Select(static p => $"{p.Key}={p.Value}")
                );

                return $"{Describe(s.Identity)}:{s.State}:{s.Role ?? "-"}:{{{metadata}}}";
            })
        );
    }
}

/// <summary>
/// The reference behavior of a relational membership store, in memory, on a virtual clock that stands still: every
/// stored instant is an offset from "now", aging moves offsets back, and a skew moves one node's offsets forward.
/// </summary>
public sealed class MembershipOracleModel(MembershipOracleScope scope) : IMembershipOracleTarget
{
    private static readonly TimeSpan _Retention =
        MembershipOracleGenerator.DeadThreshold + MembershipOracleGenerator.DeadRetentionWindow;

    private readonly Dictionary<string, (long Current, TimeSpan UpdatedAt)> _generations = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, long), Descriptor> _descriptors = [];
    private readonly Dictionary<(string, long), Liveness> _liveness = [];

    public Task<string> ExecuteAsync(MembershipOracleOp op, CancellationToken cancellationToken)
    {
        var outcome = op switch
        {
            MembershipOracleOp.Allocate a => _Allocate(a.Node),
            MembershipOracleOp.Register r => _Register(scope.Identity(r.Node, r.Slot), r.Descriptor),
            MembershipOracleOp.Heartbeat h => "heartbeat:" + _Heartbeat(scope.Identity(h.Node, h.Slot)),
            MembershipOracleOp.Leave l => _Leave(scope.Identity(l.Node, l.Slot)),
            MembershipOracleOp.ReadSnapshot => "snapshot:[" + scope.Describe(_Snapshot()) + "]",
            MembershipOracleOp.ReadNode n => "read:"
                + (_ReadNode(scope.Identity(n.Node, n.Slot))?.ToString() ?? "absent"),
            MembershipOracleOp.ReadLive => "live:[" + string.Join(' ', _Live().Select(scope.Describe)) + "]",
            MembershipOracleOp.AdvanceTime t => _Shift(node: null, -t.By),
            MembershipOracleOp.Skew s => _Shift(scope.History.Nodes[s.Node], s.By),
            _ => throw new InvalidOperationException($"Unknown operation {op}."),
        };

        return Task.FromResult($"{outcome} | {_Rows().Describe(scope.Name)}");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private string _Allocate(int node)
    {
        var nodeId = scope.History.Nodes[node];
        var next = (_generations.TryGetValue(nodeId, out var generation) ? generation.Current : 0) + 1;
        _generations[nodeId] = (next, TimeSpan.Zero);
        scope.Allocated(node, next);

        return string.Create(CultureInfo.InvariantCulture, $"allocate:{next}");
    }

    private string _Register(NodeIdentity identity, int descriptor)
    {
        var key = _Key(identity);

        if (!_IsCurrent(identity))
        {
            return "register";
        }

        // Write-once descriptor; liveness revived without moving its beat backwards.
        var (role, metadata) = MembershipOracleScope.Descriptors[descriptor];
        _descriptors.TryAdd(key, new Descriptor(role, metadata, TimeSpan.Zero));
        _liveness[key] = _liveness.TryGetValue(key, out var stored)
            ? new Liveness(_Later(stored.LastBeat), LeftAt: null)
            : new Liveness(TimeSpan.Zero, LeftAt: null);

        return "register";
    }

    private bool _Heartbeat(NodeIdentity identity)
    {
        var key = _Key(identity);

        if (
            !_IsCurrent(identity)
            || !_liveness.TryGetValue(key, out var stored)
            || stored.LeftAt is not null
            || _Age(stored.LastBeat) >= MembershipOracleGenerator.DeadThreshold
        )
        {
            return false;
        }

        _liveness[key] = stored with { LastBeat = _Later(stored.LastBeat) };

        return true;
    }

    private string _Leave(NodeIdentity identity)
    {
        var key = _Key(identity);

        // Not gated on the generation: leaving a superseded incarnation touches only its own row.
        if (_liveness.TryGetValue(key, out var stored) && stored.LeftAt is null)
        {
            _liveness[key] = stored with { LeftAt = TimeSpan.Zero };
        }

        return "leave";
    }

    private List<NodeLivenessSnapshot> _Snapshot()
    {
        foreach (var (key, row) in _liveness.ToList())
        {
            if (_Age(row.LastBeat) >= _Retention)
            {
                _liveness.Remove(key);
            }
        }

        foreach (var (key, row) in _descriptors.ToList())
        {
            if (_Age(row.CreatedAt) >= _Retention && !_liveness.ContainsKey(key))
            {
                _descriptors.Remove(key);
            }
        }

        return
        [
            .. _liveness
                .Where(entry => _IsCurrent(entry.Key))
                .Select(entry =>
                {
                    var descriptor = _descriptors.GetValueOrDefault(entry.Key);

                    return new NodeLivenessSnapshot(
                        _Identity(entry.Key),
                        _State(entry.Value),
                        descriptor?.Role,
                        descriptor?.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal)
                    );
                })
                .OrderBy(static s => s.Identity.ToString(), StringComparer.Ordinal),
        ];
    }

    private NodeLivenessState? _ReadNode(NodeIdentity identity)
    {
        var key = _Key(identity);

        return _IsCurrent(key) && _liveness.TryGetValue(key, out var row) && _Age(row.LastBeat) < _Retention
            ? _State(row)
            : null;
    }

    private IEnumerable<NodeIdentity> _Live()
    {
        return _liveness
            .Where(entry =>
                _IsCurrent(entry.Key)
                && entry.Value.LeftAt is null
                && _Age(entry.Value.LastBeat) < MembershipOracleGenerator.SuspicionThreshold
            )
            .Select(entry => _Identity(entry.Key))
            .OrderBy(static identity => identity.ToString(), StringComparer.Ordinal);
    }

    private string _Shift(string? node, TimeSpan delta)
    {
        bool moves(string nodeId) => node is null || string.Equals(nodeId, node, StringComparison.Ordinal);

        foreach (var (nodeId, generation) in _generations.ToList())
        {
            if (moves(nodeId))
            {
                _generations[nodeId] = generation with { UpdatedAt = generation.UpdatedAt + delta };
            }
        }

        foreach (var (key, row) in _descriptors.ToList())
        {
            if (moves(key.Item1))
            {
                _descriptors[key] = row with { CreatedAt = row.CreatedAt + delta };
            }
        }

        foreach (var (key, row) in _liveness.ToList())
        {
            if (moves(key.Item1))
            {
                _liveness[key] = new Liveness(row.LastBeat + delta, row.LeftAt + delta);
            }
        }

        return node is null ? "advance" : "skew";
    }

    private StoredMembership _Rows()
    {
        return new StoredMembership(
            [.. _generations.Select(static g => (g.Key, g.Value.Current))],
            [.. _liveness.Select(static l => (l.Key.Item1, l.Key.Item2, l.Value.LeftAt is not null))],
            [.. _descriptors.Keys.Select(static k => (k.Item1, k.Item2))]
        );
    }

    private static NodeLivenessState _State(Liveness row)
    {
        var age = _Age(row.LastBeat);

        return row.LeftAt is not null || age >= MembershipOracleGenerator.DeadThreshold ? NodeLivenessState.Dead
            : age >= MembershipOracleGenerator.SuspicionThreshold ? NodeLivenessState.Suspected
            : NodeLivenessState.Alive;
    }

    private bool _IsCurrent(NodeIdentity identity) => _IsCurrent(_Key(identity));

    private bool _IsCurrent((string NodeId, long Incarnation) key)
    {
        return _generations.TryGetValue(key.NodeId, out var generation) && generation.Current == key.Incarnation;
    }

    // Instants are offsets from a "now" that stands still, so an instant's age is its offset negated.
    private static TimeSpan _Age(TimeSpan instant) => -instant;

    // A heartbeat writes the later of the stored beat and the clock.
    private static TimeSpan _Later(TimeSpan stored) => stored > TimeSpan.Zero ? stored : TimeSpan.Zero;

    private static (string, long) _Key(NodeIdentity identity) => (identity.NodeId.Value, identity.Incarnation.Value);

    private static NodeIdentity _Identity((string NodeId, long Incarnation) key)
    {
        return new NodeIdentity(new NodeId(key.NodeId), new NodeIncarnation(key.Incarnation));
    }

    private sealed record Descriptor(string? Role, Dictionary<string, string> Metadata, TimeSpan CreatedAt);

    private sealed record Liveness(TimeSpan LastBeat, TimeSpan? LeftAt);
}
