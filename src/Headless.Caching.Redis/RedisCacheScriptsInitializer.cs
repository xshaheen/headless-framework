// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Redis;

namespace Headless.Caching;

internal sealed class RedisCacheScriptsInitializer(HeadlessRedisScriptsLoader scriptsLoader) : HostedInitializer
{
    private readonly List<RedisScriptDefinition> _definitions =
    [
        IncrementWithExpireScriptDefinition.Instance,
        RemoveIfEqualScriptDefinition.Instance,
        ReplaceIfEqualScriptDefinition.Instance,
        SetIfHigherScriptDefinition.Instance,
        SetIfLowerScriptDefinition.Instance,
    ];

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await scriptsLoader.LoadAsync(_definitions, cancellationToken).ConfigureAwait(false);
    }
}
