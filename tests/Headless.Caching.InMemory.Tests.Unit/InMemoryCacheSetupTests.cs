// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>Happy-path coverage for the <c>UseInMemory</c> default registration.</summary>
public sealed class InMemoryCacheSetupTests : TestBase
{
    [Fact]
    public async Task use_in_memory_should_register_default_cache_role_keys_and_adapters()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddHeadlessCaching(setup => setup.UseInMemory());
        await using var provider = services.BuildServiceProvider();

        // when
        var defaultCache = provider.GetRequiredService<ICache>();

        // then - the unkeyed default is the in-memory cache, aliased under the "memory" role key
        defaultCache.Should().BeOfType<InMemoryCache>();
        provider.GetRequiredService<IInMemoryCache>().Should().BeSameAs(defaultCache);
        provider.GetRequiredKeyedService<ICache>(CacheConstants.MemoryCacheProvider).Should().BeSameAs(defaultCache);

        // then - the generic cache adapter resolves over the default cache
        provider.GetRequiredService<ICache<InMemoryCacheSetupTests>>().Should().NotBeNull();

        // then - the remote adapter wraps the same store and is aliased under the "remote" role key
        var remote = provider.GetRequiredService<IRemoteCache>();
        provider.GetRequiredKeyedService<IRemoteCache>(CacheConstants.RemoteCacheProvider).Should().BeSameAs(remote);
        await defaultCache.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);
        (await remote.GetAsync<string>("key", AbortToken)).Value.Should().Be("value");
    }
}
