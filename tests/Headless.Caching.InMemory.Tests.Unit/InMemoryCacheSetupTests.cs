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
    public void should_register_default_cache_role_keys_and_adapters_when_use_in_memory()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddHeadlessCaching(setup => setup.UseInMemory());
        using var provider = services.BuildServiceProvider();

        // when
        var defaultCache = provider.GetRequiredService<ICache>();

        // then - the unkeyed default is the in-memory cache, aliased under the memory role key
        defaultCache.Should().BeOfType<InMemoryCache>();
        provider.GetRequiredService<IInMemoryCache>().Should().BeSameAs(defaultCache);
        provider.GetRequiredKeyedService<ICache>(CacheConstants.MemoryCacheProvider).Should().BeSameAs(defaultCache);

        // then - the generic cache adapter resolves over the default cache
        provider.GetRequiredService<ICache<InMemoryCacheSetupTests>>().Should().NotBeNull();

        // then - no IRemoteCache is registered for an InMemory-only setup
        provider.GetService<IRemoteCache>().Should().BeNull();
    }
}
