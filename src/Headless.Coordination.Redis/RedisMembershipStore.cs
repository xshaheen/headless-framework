// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Coordination.Redis.Scripts;
using Headless.Redis;
using Headless.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Coordination.Redis;

internal sealed class RedisMembershipStore(
    IConnectionMultiplexer multiplexer,
    [FromKeyedServices(RedisCoordinationServiceKeys.ScriptsLoader)] HeadlessRedisScriptsLoader scriptsLoader,
    IOptions<CoordinationOptions> coordinationOptions,
    IOptions<RedisCoordinationOptions> redisOptions,
    [FromKeyedServices(CoordinationOptions.JsonSerializerServiceKey)] IJsonSerializer serializer
) : IMembershipStore
{
    private readonly ConcurrentDictionary<NodeIdentity, NodeDescriptor> _descriptors = new();

    // Metadata serialized once at descriptor upsert and reused on the hot heartbeat path.
    private readonly ConcurrentDictionary<NodeIdentity, string> _metadataJson = new();

    private CoordinationOptions Options => coordinationOptions.Value;

    private RedisCoordinationOptions RedisOptions => redisOptions.Value;

    private IDatabase Db => multiplexer.GetDatabase();

    public async ValueTask<NodeIncarnation> AllocateIncarnationAsync(
        NodeId nodeId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = await scriptsLoader
            .EvaluateAsync(
                Db,
                RedisMembershipAllocateIncarnationScriptDefinition.Instance,
                new AllocateIncarnationParams(_GenKey(nodeId), _KnownKey(), _GenerationField(nodeId)),
                cancellationToken
            )
            .ConfigureAwait(false);

        // The allocate script returns redis.call('incr'), which is always an integer and never nil, so this cast is unconditionally safe.
        return new NodeIncarnation((long)value);
    }

    public async ValueTask UpsertDescriptorAsync(
        NodeDescriptor descriptor,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Write-through cache: keep role/metadata local for the hot heartbeat path...
        _descriptors[descriptor.Identity] = descriptor;
        _metadataJson[descriptor.Identity] = _SerializeDictionary(descriptor.Metadata);

        // ...and durably establish liveness at register by running the same guarded write the first
        // heartbeat would have done (HSET role/metadata into :known, ZADD member into :live with server
        // TIME, gated on the freshly-allocated incarnation). A stale/impossible incarnation is rejected.
        await scriptsLoader
            .EvaluateAsync(
                Db,
                RedisMembershipHeartbeatScriptDefinition.Instance,
                _CreateHeartbeatParams(descriptor.Identity, descriptor, allowCreate: true),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> HeartbeatAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _descriptors.TryGetValue(identity, out var descriptor);

        // Cleanup is owned by RedisMembershipCleanupService (5-min interval); the heartbeat path no longer prunes.
        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                RedisMembershipHeartbeatScriptDefinition.Instance,
                _CreateHeartbeatParams(identity, descriptor, allowCreate: false),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
    }

    public async ValueTask LeaveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _descriptors.TryGetValue(identity, out var descriptor);

        await scriptsLoader
            .EvaluateAsync(
                Db,
                RedisMembershipLeaveScriptDefinition.Instance,
                new LeaveParams(
                    _KnownKey(),
                    _LiveKey(),
                    identity.ToString(),
                    _ToMilliseconds(Options.DeadThreshold),
                    descriptor?.Role ?? string.Empty,
                    _MetadataJson(identity, descriptor)
                ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    // The read path performs opportunistic writes (stale-member prune and generation-mirror backfill) and
    // therefore must target a writable Redis primary; routing it to a read-only replica will fail.
    public async ValueTask<IReadOnlyList<NodeLivenessSnapshot>> ReadLivenessAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                RedisMembershipReadScriptDefinition.Instance,
                new ReadParams(
                    _KnownKey(),
                    _LiveKey(),
                    _GenKeyPrefix(),
                    _GenerationFieldPrefix,
                    _ToMilliseconds(Options.SuspicionThreshold),
                    _ToMilliseconds(Options.DeadThreshold),
                    _OperationalPruneMilliseconds(),
                    nameof(NodeLivenessState.Alive),
                    nameof(NodeLivenessState.Suspected),
                    nameof(NodeLivenessState.Dead)
                ),
                cancellationToken
            )
            .ConfigureAwait(false);

        return _ParseSnapshots((RedisResult[]?)result);
    }

    // Targeted single-member read. Unlike ReadLivenessAsync this performs no writes (no prune, no mirror
    // backfill), so it does not require a writable primary — but it is left on the default database for
    // simplicity. Resolves generation mirror-first with gen: fallback and applies the operational prune
    // window as a read-only absent cutoff, matching the snapshot's delete-and-omit.
    public async ValueTask<NodeLivenessState?> ReadNodeLivenessAsync(
        NodeIdentity identity,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                RedisMembershipReadNodeLivenessScriptDefinition.Instance,
                new ReadNodeLivenessParams(
                    _KnownKey(),
                    _GenKey(identity.NodeId),
                    _GenerationField(identity.NodeId),
                    identity.ToString(),
                    identity.Incarnation.Value,
                    _ToMilliseconds(Options.SuspicionThreshold),
                    _ToMilliseconds(Options.DeadThreshold),
                    _OperationalPruneMilliseconds(),
                    nameof(NodeLivenessState.Alive),
                    nameof(NodeLivenessState.Suspected),
                    nameof(NodeLivenessState.Dead)
                ),
                cancellationToken
            )
            .ConfigureAwait(false);

        return result.IsNull ? null : Enum.Parse<NodeLivenessState>((string)result!);
    }

    // Live-node fast path: read Alive current-generation identities straight from the :live sorted set
    // (ZRANGEBYSCORE) instead of HGETALL-scanning and decoding the whole :known hash. Read-only — no prune, no
    // mirror backfill — so it does not require a writable primary, but it is left on the default database for
    // simplicity. The script resolves each candidate's generation mirror-first with a gen: fallback so a
    // superseded incarnation is excluded even while still alive-by-score.
    public async ValueTask<IReadOnlyList<NodeIdentity>> ReadLiveNodesAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                RedisMembershipReadLiveNodesScriptDefinition.Instance,
                new ReadLiveNodesParams(
                    _LiveKey(),
                    _KnownKey(),
                    _GenKeyPrefix(),
                    _GenerationFieldPrefix,
                    _ToMilliseconds(Options.SuspicionThreshold),
                    _ToMilliseconds(Options.DeadThreshold)
                ),
                cancellationToken
            )
            .ConfigureAwait(false);

        return _ParseLiveNodes((RedisResult[]?)result);
    }

    internal async ValueTask CleanupAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await scriptsLoader
            .EvaluateAsync(
                Db,
                RedisMembershipCleanupScriptDefinition.Instance,
                new CleanupParams(_KnownKey(), _LiveKey(), _GenerationFieldPrefix, _OperationalPruneMilliseconds()),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private NodeLivenessSnapshot[] _ParseSnapshots(RedisResult[]? result)
    {
        if (result is null || result.Length == 0)
        {
            return [];
        }

        var snapshots = new List<NodeLivenessSnapshot>(result.Length);

        foreach (var item in result)
        {
            var fields = (RedisResult[]?)item;

            if (fields is null || fields.Length < 4)
            {
                // Local contract violation on the script's return shape, not an error raised by the Redis
                // server, so InvalidOperationException (matching _ClusterKey below) is the precise type.
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Unexpected coordination membership read script result shape (length={fields?.Length ?? 0}, expected >= 4)."
                    )
                );
            }

            var identity = NodeIdentity.Parse((string)fields[0]!);
            var state = Enum.Parse<NodeLivenessState>((string)fields[1]!);
            var role = (string)fields[2]!;
            var metadataJson = (string)fields[3]!;

            snapshots.Add(
                new NodeLivenessSnapshot(
                    identity,
                    state,
                    string.IsNullOrEmpty(role) ? null : role,
                    _DeserializeDictionary(metadataJson)
                )
            );
        }

        // The read Lua already returns members sorted (table.sort); no C# re-sort is needed.
        return [.. snapshots];
    }

    private static NodeIdentity[] _ParseLiveNodes(RedisResult[]? result)
    {
        if (result is null || result.Length == 0)
        {
            return [];
        }

        var identities = new NodeIdentity[result.Length];

        // The script already returns members sorted (table.sort); no C# re-sort is needed.
        for (var i = 0; i < result.Length; i++)
        {
            identities[i] = NodeIdentity.Parse((string)result[i]!);
        }

        return identities;
    }

    private string _MetadataJson(NodeIdentity identity, NodeDescriptor? descriptor)
    {
        return _metadataJson.TryGetValue(identity, out var cached)
            ? cached
            : _SerializeDictionary(descriptor?.Metadata);
    }

    private RedisKey _LiveKey()
    {
        return _ClusterKey("live");
    }

    private RedisKey _KnownKey()
    {
        return _ClusterKey("known");
    }

    private RedisKey _GenKey(NodeId nodeId)
    {
        return _GenKeyPrefix() + nodeId.Value;
    }

    private const string _GenerationFieldPrefix = "__gen:";

    private static string _GenerationField(NodeId nodeId)
    {
        return _GenerationFieldPrefix + nodeId.Value;
    }

    private string _GenKeyPrefix()
    {
        return $"{Options.KeyPrefix}{{{Options.ClusterName}}}:gen:";
    }

    private RedisKey _ClusterKey(string suffix)
    {
        if (
            Options.ClusterName.Contains('{', StringComparison.Ordinal)
            || Options.ClusterName.Contains('}', StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                "Redis coordination cluster names cannot contain Redis hash-tag braces."
            );
        }

        return $"{Options.KeyPrefix}{{{Options.ClusterName}}}:{suffix}";
    }

    private long _OperationalPruneMilliseconds()
    {
        var minimum = Options.DeadThreshold + Options.DeadRetentionWindow;
        var retention = RedisOptions.RedisKnownNodeRetention < minimum ? minimum : RedisOptions.RedisKnownNodeRetention;

        return _ToMilliseconds(retention);
    }

    private HeartbeatParams _CreateHeartbeatParams(
        NodeIdentity identity,
        NodeDescriptor? descriptor,
        bool allowCreate
    ) =>
        new(
            _LiveKey(),
            _KnownKey(),
            _GenKey(identity.NodeId),
            _GenerationField(identity.NodeId),
            identity.ToString(),
            identity.Incarnation.Value,
            _ToMilliseconds(Options.DeadThreshold),
            allowCreate ? 1 : 0,
            descriptor?.Role ?? string.Empty,
            _MetadataJson(identity, descriptor)
        );

    private static long _ToMilliseconds(TimeSpan value)
    {
        return (long)Math.Ceiling(value.TotalMilliseconds);
    }

    private string _SerializeDictionary(IReadOnlyDictionary<string, string>? value)
    {
        return serializer.SerializeToString(value ?? new Dictionary<string, string>(StringComparer.Ordinal)) ?? "{}";
    }

    private Dictionary<string, string> _DeserializeDictionary(string value)
    {
        return serializer.Deserialize<Dictionary<string, string>>(value)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
