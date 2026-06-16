// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Tests for the <c>UseBclCache</c> registration.</summary>
public sealed class SetupBclCacheTests
{
    private const string _CacheName = "bcl";

    [Fact]
    public void should_register_the_distributed_cache_adapter()
    {
        // given
        var services = new ServiceCollection();

        // when — byte[] is the cache's native wire format, so the callback only selects the backing provider; no
        // serializer is wired (stub providers stand in for a real backend so the unit test stays dependency-free)
        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.UseBclCache(
                options =>
                {
                    options.CacheName = _CacheName;
                    options.DefaultAbsoluteExpiration = TimeSpan.FromHours(1);
                },
                instance => instance.RegisterProvider(static _ => { })
            );
        });

        // then — the adapter owns the IDistributedCache slot
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IDistributedCache));
    }
}
