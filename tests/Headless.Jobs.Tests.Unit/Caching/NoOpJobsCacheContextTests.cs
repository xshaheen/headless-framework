// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Interfaces;
using Headless.Jobs.Temps;

namespace Tests.Caching;

public sealed class NoOpJobsCacheContextTests
{
    [Fact]
    public async Task GetOrSetArrayAsync_invokes_the_factory_and_returns_its_result()
    {
        IJobsCacheContext context = new NoOpJobsCacheContext();
        var factoryCalled = false;
        var expected = new[] { "a", "b" };

        var result = await context.GetOrSetArrayAsync<string>(
            "key",
            _ =>
            {
                factoryCalled = true;

                return Task.FromResult<string[]?>(expected);
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        factoryCalled.Should().BeTrue();
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public void HasRedisConnection_is_false_so_the_provider_skips_cache_invalidation()
    {
        IJobsCacheContext context = new NoOpJobsCacheContext();

        context.HasRedisConnection.Should().BeFalse();
    }
}
