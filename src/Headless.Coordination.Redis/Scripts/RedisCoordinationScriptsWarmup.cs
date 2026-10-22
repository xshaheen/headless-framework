// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Redis;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Coordination.Redis.Scripts;

/// <summary>
/// Loads the Redis membership store's Lua scripts after the host starts, so the first registration and heartbeat run
/// their scripts by SHA instead of paying the load.
/// </summary>
/// <remarks>
/// It does not gate readiness: a script not loaded yet still loads on its first use. A failure is retried with the
/// warm-up back-off.
/// </remarks>
internal sealed class RedisCoordinationScriptsWarmup(
    [FromKeyedServices(RedisCoordinationServiceKeys.ScriptsLoader)] HeadlessRedisScriptsLoader loader
) : IWarmup
{
    private static readonly IReadOnlyList<RedisScriptDefinition> _Scripts =
    [
        RedisMembershipAllocateIncarnationScriptDefinition.Instance,
        RedisMembershipHeartbeatScriptDefinition.Instance,
        RedisMembershipReadScriptDefinition.Instance,
        RedisMembershipReadNodeLivenessScriptDefinition.Instance,
        RedisMembershipReadLiveNodesScriptDefinition.Instance,
        RedisMembershipLeaveScriptDefinition.Instance,
        RedisMembershipCleanupScriptDefinition.Instance,
    ];

    public string Name => "RedisCoordinationScripts";

    public bool GatesReadiness => false;

    public Task WarmUpAsync(CancellationToken cancellationToken) =>
        loader.LoadAsync(_Scripts, cancellationToken).AsTask();
}
