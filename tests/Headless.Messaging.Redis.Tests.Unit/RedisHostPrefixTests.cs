// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class RedisHostPrefixTests : TestBase
{
    [Fact]
    public void should_keep_stream_keys_unchanged_without_a_prefix()
    {
        RedisPhysicalAddress.BusStream("", "orders.created").Should().Be("headless:messaging:bus:orders.created");
        RedisPhysicalAddress.QueueStream("", "orders.created").Should().Be("headless:messaging:queue:orders.created");
    }

    [Fact]
    public void should_put_the_prefix_in_front_of_every_stream_key_and_its_wake_channel()
    {
        var stream = RedisPhysicalAddress.ForLane("wt1", MessageLane.Bus, "orders.created");

        stream.Should().Be("wt1:headless:messaging:bus:orders.created");
        RedisPhysicalAddress
            .QueueStream("wt1", "orders.created")
            .Should()
            .Be("wt1:headless:messaging:queue:orders.created");
        RedisPhysicalAddress.WakeChannel(stream).Should().Be("wt1:headless:messaging:bus:orders.created:wake");
    }

    [Fact]
    public void should_keep_consumer_groups_unprefixed_because_the_stream_key_scopes_them()
    {
        // A group exists only inside its stream, so a prefixed stream already keeps an environment's groups apart.
        RedisPhysicalAddress.ConsumerGroup(MessageLane.Bus, "billing").Should().Be("billing");
    }

    [Fact]
    public async Task should_apply_the_host_prefix_to_the_transport_options()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHeadlessPrefix("wt1");

        // when
        services.AddHeadlessMessaging(setup => setup.UseRedis("localhost:6379"));
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<RedisMessagingOptions>>().Value.HostPrefix.Should().Be("wt1");
    }

    [Fact]
    public async Task should_leave_the_transport_options_unprefixed_without_a_prefix()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup => setup.UseRedis("localhost:6379"));
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<RedisMessagingOptions>>().Value.HostPrefix.Should().BeEmpty();
    }
}
