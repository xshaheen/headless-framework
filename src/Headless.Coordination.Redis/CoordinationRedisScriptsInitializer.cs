// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Redis;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Coordination.Redis;

internal sealed class CoordinationRedisScriptsInitializer(
    [FromKeyedServices(RedisCoordinationServiceKeys.ScriptsLoader)] HeadlessRedisScriptsLoader loader
) : HostedInitializer
{
    private static readonly IReadOnlyList<RedisScriptDefinition> _Definitions =
    [
        RedisMembershipAllocateIncarnationScriptDefinition.Instance,
        RedisMembershipHeartbeatScriptDefinition.Instance,
        RedisMembershipReadScriptDefinition.Instance,
        RedisMembershipReadNodeLivenessScriptDefinition.Instance,
        RedisMembershipReadLiveNodesScriptDefinition.Instance,
        RedisMembershipLeaveScriptDefinition.Instance,
        RedisMembershipCleanupScriptDefinition.Instance,
    ];

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await loader.LoadAsync(_Definitions, cancellationToken).ConfigureAwait(false);
    }
}
