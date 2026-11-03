// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Features;

/// <summary>
/// The cache feature values live in, resolved once from <see cref="FeatureManagementOptions.FeatureValueCacheName"/>
/// so the value store that reads it and the storage providers that evict from it always use the same instance.
/// </summary>
internal sealed class FeatureValueCache(ICache cache)
{
    public ICache Cache { get; } = cache;

    public static FeatureValueCache Create(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<FeatureManagementOptions>>().Value;
        var cache = string.IsNullOrEmpty(options.FeatureValueCacheName)
            ? services.GetRequiredService<ICache>()
            : services.GetRequiredService<ICacheProvider>().GetCache(options.FeatureValueCacheName);

        return new FeatureValueCache(cache);
    }
}
