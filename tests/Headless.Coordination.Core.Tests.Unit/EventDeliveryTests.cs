// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class EventDeliveryTests : TestBase
{
    [Fact]
    public async Task should_not_block_other_subscribers_when_one_subscriber_is_not_reading()
    {
        // given
        var source = new MembershipEventSource(NullLogger<MembershipEventSource>.Instance, capacity: 1);
        await using var slow = source.WatchAsync(AbortToken).GetAsyncEnumerator(AbortToken);
        await using var fast = source.WatchAsync(AbortToken).GetAsyncEnumerator(AbortToken);
        var identity = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));

        // when
        source.Publish(new NodeJoined(identity));
        source.Publish(new NodeSuspected(identity));

        // then
        (await fast.MoveNextAsync()).Should().BeTrue();
        fast.Current.Identity.Should().Be(identity);
        slow.Should().NotBeNull();
    }

    [Fact]
    public async Task should_fan_out_events_to_multiple_subscribers()
    {
        // given
        var source = new MembershipEventSource(NullLogger<MembershipEventSource>.Instance);
        await using var first = source.WatchAsync(AbortToken).GetAsyncEnumerator(AbortToken);
        await using var second = source.WatchAsync(AbortToken).GetAsyncEnumerator(AbortToken);
        var identity = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));

        // when
        source.Publish(new NodeLeft(identity));

        // then
        (await first.MoveNextAsync()).Should().BeTrue();
        first.Current.Should().BeOfType<NodeLeft>().Which.Identity.Should().Be(identity);
        (await second.MoveNextAsync()).Should().BeTrue();
        second.Current.Should().BeOfType<NodeLeft>().Which.Identity.Should().Be(identity);
    }
}
