// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Redis;

namespace Headless.DistributedLocks.Redis;

internal sealed class RedisMutexScriptsInitializer(HeadlessRedisScriptsLoader scriptsLoader) : HostedInitializer
{
    private readonly List<RedisScriptDefinition> _definitions =
    [
        TryAcquireLockWithFenceScriptDefinition.Instance,
        RemoveIfEqualScriptDefinition.Instance,
        ReplaceIfEqualScriptDefinition.Instance,
    ];


    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await scriptsLoader.LoadAsync(_definitions, cancellationToken).ConfigureAwait(false);
    }
}
