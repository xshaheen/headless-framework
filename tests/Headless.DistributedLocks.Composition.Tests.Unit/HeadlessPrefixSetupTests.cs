// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class HeadlessPrefixSetupTests : TestBase
{
    [Fact]
    public void should_put_the_host_prefix_in_front_of_the_lock_key_prefix()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureHeadlessPrefix("wt1");

        // when
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
        using var provider = services.BuildServiceProvider();

        // then - a lock on "orders" reaches the store as wt1:distributed-lock:orders
        provider
            .GetRequiredService<IOptions<DistributedLockOptions>>()
            .Value.KeyPrefix.Should()
            .Be("wt1:distributed-lock:");
    }

    [Fact]
    public void should_put_the_host_prefix_in_front_of_a_configured_lock_key_prefix()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureHeadlessPrefix("wt1");

        // when
        services.AddHeadlessDistributedLocks(setup =>
        {
            setup.ConfigureOptions(options => options.KeyPrefix = "locks:");
            setup.UseInMemory();
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<DistributedLockOptions>>().Value.KeyPrefix.Should().Be("wt1:locks:");
    }

    [Fact]
    public void should_leave_the_lock_key_prefix_unchanged_when_the_host_has_no_prefix()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredService<IOptions<DistributedLockOptions>>()
            .Value.KeyPrefix.Should()
            .Be(DistributedLockOptions.DefaultKeyPrefix);
    }
}
