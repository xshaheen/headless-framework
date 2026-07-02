// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching.Scripts;
using Headless.Hosting.Initialization;
using Headless.Redis;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Caching;

internal sealed class RedisCacheScriptsInitializer(
    [FromKeyedServices(RedisCacheServiceKeys.ScriptsLoader)] HeadlessRedisScriptsLoader scriptsLoader
) : HostedInitializer
{
    private static readonly IReadOnlyList<RedisScriptDefinition> _Definitions =
    [
        IncrementWithExpireScriptDefinition.Instance,
        CacheRemoveIfEqualScriptDefinition.Instance,
        CacheReplaceIfEqualScriptDefinition.Instance,
        CacheTaggedSetScriptDefinition.Instance,
        SetIfHigherScriptDefinition.Instance,
        SetIfLowerScriptDefinition.Instance,
        SlidingRearmScriptDefinition.Instance,
    ];

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await scriptsLoader.LoadAsync(_Definitions, cancellationToken).ConfigureAwait(false);
    }
}
