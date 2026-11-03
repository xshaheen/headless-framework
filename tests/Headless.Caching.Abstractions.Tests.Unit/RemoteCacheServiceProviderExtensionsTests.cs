// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Tests;

public sealed class RemoteCacheServiceProviderExtensionsTests : TestBase
{
    [Fact]
    public void should_return_the_remote_tier_when_one_is_registered()
    {
        // given
        var remote = Substitute.For<IRemoteCache>();
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICache>());
        services.AddSingleton(remote);
        using var provider = services.BuildServiceProvider();

        // when
        var cache = provider.GetRemoteOrDefaultCache();

        // then
        cache.Should().BeSameAs(remote);
    }

    [Fact]
    public void should_return_the_default_cache_when_no_remote_tier_is_registered()
    {
        // given
        var defaultCache = Substitute.For<ICache>();
        var services = new ServiceCollection();
        services.AddSingleton(defaultCache);
        using var provider = services.BuildServiceProvider();

        // when
        var cache = provider.GetRemoteOrDefaultCache();

        // then
        cache.Should().BeSameAs(defaultCache);
    }

    [Fact]
    public void should_throw_when_no_cache_is_registered()
    {
        // given
        using var provider = new ServiceCollection().BuildServiceProvider();

        // when
        var act = () => provider.GetRemoteOrDefaultCache();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddHeadlessCaching*");
    }
}
