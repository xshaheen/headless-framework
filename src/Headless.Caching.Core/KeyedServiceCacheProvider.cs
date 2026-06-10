// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Caching;

/// <summary>
/// <see cref="ICacheProvider"/> over the container's keyed <see cref="ICache"/> registrations — resolves both
/// named instances (added through the name-taking setup overloads) and the reserved role keys.
/// </summary>
internal sealed class KeyedServiceCacheProvider(IServiceProvider serviceProvider) : ICacheProvider
{
    public ICache GetCache(string name)
    {
        Argument.IsNotNullOrEmpty(name);

        return serviceProvider.GetKeyedService<ICache>(name)
            ?? throw new InvalidOperationException(
                $"No cache is registered under the name '{name}'. Register a named instance first — for example "
                    + $"AddInMemoryCache(\"{name}\", …), AddRedisCache(\"{name}\", …), or AddHybridCache(\"{name}\", …) — "
                    + $"or use one of the role keys ('{CacheConstants.MemoryCacheProvider}', '{CacheConstants.RemoteCacheProvider}', "
                    + $"'{CacheConstants.HybridCacheProvider}') registered by the corresponding cache setup."
            );
    }

    public ICache? GetCacheOrNull(string name)
    {
        Argument.IsNotNullOrEmpty(name);

        return serviceProvider.GetKeyedService<ICache>(name);
    }
}
