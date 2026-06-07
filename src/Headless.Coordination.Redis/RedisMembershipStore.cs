// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
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

        var value = await Db.StringIncrementAsync(_GenKey(nodeId)).ConfigureAwait(false);

        return new NodeIncarnation(value);
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
                new HeartbeatParams(
                    _LiveKey(),
                    _KnownKey(),
                    _GenKey(descriptor.Identity.NodeId),
                    descriptor.Identity.ToString(),
                    descriptor.Identity.Incarnation.Value,
                    _ToMilliseconds(Options.DeadThreshold),
                    descriptor.Role ?? string.Empty,
                    _MetadataJson(descriptor.Identity, descriptor)
                ),
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
                new HeartbeatParams(
                    _LiveKey(),
                    _KnownKey(),
                    _GenKey(identity.NodeId),
                    identity.ToString(),
                    identity.Incarnation.Value,
                    _ToMilliseconds(Options.DeadThreshold),
                    descriptor?.Role ?? string.Empty,
                    _MetadataJson(identity, descriptor)
                ),
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

    internal async ValueTask CleanupAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await scriptsLoader
            .EvaluateAsync(
                Db,
                RedisMembershipCleanupScriptDefinition.Instance,
                new CleanupParams(_KnownKey(), _LiveKey(), _OperationalPruneMilliseconds()),
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
                    $"Unexpected coordination membership read script result shape (length={fields?.Length ?? 0}, expected >= 4)."
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
        return snapshots.ToArray();
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
