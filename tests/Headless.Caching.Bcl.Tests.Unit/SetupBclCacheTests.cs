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

    [Fact]
    public void should_reject_a_serializer_configured_on_the_named_instance()
    {
        // given
        var services = new ServiceCollection();

        // when — byte[] is the cache's native wire format, so a serializer on the instance is meaningless; the guard
        // fails fast rather than silently ignoring it. WithSerializer (Redis package) sets the same SerializerFactory
        // this checks; the factory is never invoked, so a null-returning stub is enough to make it non-null.
        var action = () =>
            services.AddHeadlessCaching(setup =>
            {
                setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
                setup.UseBclCache(
                    options =>
                    {
                        options.CacheName = _CacheName;
                        options.DefaultAbsoluteExpiration = TimeSpan.FromHours(1);
                    },
                    instance =>
                    {
                        instance.RegisterProvider(static _ => { });
                        instance.SetSerializerFactory(static _ => null!);
                    }
                );
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*serializer*");
    }
}
