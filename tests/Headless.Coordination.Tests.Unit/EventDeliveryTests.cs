// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Threading.Channels;
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

        // when: fill the slow subscriber's single-slot channel, then publish past its capacity.
        source.Publish(new NodeJoined(identity));
        (await fast.MoveNextAsync()).Should().BeTrue();
        fast.Current.Should().BeOfType<NodeJoined>();

        // The slow subscriber never reads, so its channel is now full and the next event overflows it.
        source.Publish(new NodeSuspected(identity));

        // then: the fast subscriber still receives the new event despite the slow subscriber being full
        // (proving Publish is non-blocking and drops for the lagging subscriber only).
        (await fast.MoveNextAsync())
            .Should()
            .BeTrue();
        fast.Current.Should().BeOfType<NodeSuspected>().Which.Identity.Should().Be(identity);

        // The slow subscriber only ever observed the first event; the overflow was dropped, not blocked.
        (await slow.MoveNextAsync())
            .Should()
            .BeTrue();
        slow.Current.Should().BeOfType<NodeJoined>();
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
        (await first.MoveNextAsync())
            .Should()
            .BeTrue();
        first.Current.Should().BeOfType<NodeLeft>().Which.Identity.Should().Be(identity);
        (await second.MoveNextAsync()).Should().BeTrue();
        second.Current.Should().BeOfType<NodeLeft>().Which.Identity.Should().Be(identity);
    }

    [Fact]
    public void should_prune_completed_subscribers_seen_during_publish()
    {
        // given
        var source = new MembershipEventSource(NullLogger<MembershipEventSource>.Instance);
        var channel = Channel.CreateBounded<NodeMembershipEvent>(1);
        channel.Writer.TryComplete();
        var field = typeof(MembershipEventSource).GetField(
            "_subscribers",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        field.Should().NotBeNull();
        field!.SetValue(source, ImmutableArray.Create(channel));
        var identity = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));

        // when
        source.Publish(new NodeJoined(identity));

        // then
        ((ImmutableArray<Channel<NodeMembershipEvent>>)field.GetValue(source)!)
            .Should()
            .BeEmpty();
    }
}
