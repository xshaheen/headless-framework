// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Headless.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Coordination.Redis;

internal sealed class RedisMembershipStore(
    IConnectionMultiplexer multiplexer,
    [FromKeyedServices(RedisCoordinationServiceKeys.ScriptsLoader)] HeadlessRedisScriptsLoader scriptsLoader,
    IOptions<CoordinationOptions> coordinationOptions,
    IOptions<RedisCoordinationOptions> redisOptions
) : IMembershipStore
{
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<NodeIdentity, NodeDescriptor> _descriptors = new();

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

    public ValueTask UpsertDescriptorAsync(NodeDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _descriptors[descriptor.Identity] = descriptor;

        return ValueTask.CompletedTask;
    }

    public async ValueTask<bool> HeartbeatAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _descriptors.TryGetValue(identity, out var descriptor);

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
                    _SerializeDictionary(descriptor?.Metadata)
                ),
                cancellationToken
            )
            .ConfigureAwait(false);

        await CleanupAsync(cancellationToken).ConfigureAwait(false);

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
                    _SerializeDictionary(descriptor?.Metadata)
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

    private static NodeLivenessSnapshot[] _ParseSnapshots(RedisResult[]? result)
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
                throw new RedisServerException("Unexpected coordination membership read script result.");
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

        return snapshots.OrderBy(static snapshot => snapshot.Identity.ToString(), StringComparer.Ordinal).ToArray();
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
        if (Options.ClusterName.Contains('{', StringComparison.Ordinal)
            || Options.ClusterName.Contains('}', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Redis coordination cluster names cannot contain Redis hash-tag braces.");
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

    private static string _SerializeDictionary(IReadOnlyDictionary<string, string>? value)
    {
        return JsonSerializer.Serialize(value ?? new Dictionary<string, string>(StringComparer.Ordinal), _JsonOptions);
    }

    private static Dictionary<string, string> _DeserializeDictionary(string value)
    {
        return JsonSerializer.Deserialize<Dictionary<string, string>>(value, _JsonOptions)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct HeartbeatParams(
        RedisKey liveKey,
        RedisKey knownKey,
        RedisKey genKey,
        string member,
        long incarnation,
        long hardMs,
        string role,
        string metadata
    );

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct LeaveParams(
        RedisKey knownKey,
        RedisKey liveKey,
        string member,
        long hardMs,
        string role,
        string metadata
    );

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ReadParams(
        RedisKey knownKey,
        RedisKey liveKey,
        string genKeyPrefix,
        long softMs,
        long hardMs,
        long pruneMs,
        string aliveState,
        string suspectedState,
        string deadState
    );

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct CleanupParams(RedisKey knownKey, RedisKey liveKey, long pruneMs);
}
