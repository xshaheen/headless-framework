// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.RateLimiting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fakes;

namespace Tests.RateLimiting;

public sealed class SetupTests : TestBase
{
    [Fact]
    public async Task should_register_rate_limiter_with_typed_storage()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddRateLimiter<FakeDistributedRateLimiterStorage>(_ => { });
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IDistributedRateLimiter>().Should().NotBeNull();
        provider.GetRequiredService<FakeDistributedRateLimiterStorage>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_register_keyed_rate_limiter_with_typed_storage()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddKeyedRateLimiter<FakeDistributedRateLimiterStorage>("tenant", _ => { });
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredKeyedService<IDistributedRateLimiter>("tenant").Should().NotBeNull();
        provider.GetRequiredService<FakeDistributedRateLimiterStorage>().Should().NotBeNull();
    }
}
