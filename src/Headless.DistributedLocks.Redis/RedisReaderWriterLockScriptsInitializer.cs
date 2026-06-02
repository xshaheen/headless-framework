// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Redis;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.DistributedLocks.Redis;

internal sealed class RedisReaderWriterLockScriptsInitializer(
    [FromKeyedServices(RedisDistributedLockServiceKeys.ScriptsLoader)] HeadlessRedisScriptsLoader scriptsLoader
) : HostedInitializer
{
    private readonly List<RedisScriptDefinition> _definitions =
    [
        TryAcquireReadLockScriptDefinition.Instance,
        TryExtendReadLockScriptDefinition.Instance,
        ReleaseReadLockScriptDefinition.Instance,
        TryAcquireWriteLockScriptDefinition.Instance,
        TryExtendWriteLockScriptDefinition.Instance,
        ReleaseWriteLockScriptDefinition.Instance,
    ];

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await scriptsLoader.LoadAsync(_definitions, cancellationToken).ConfigureAwait(false);
    }
}
