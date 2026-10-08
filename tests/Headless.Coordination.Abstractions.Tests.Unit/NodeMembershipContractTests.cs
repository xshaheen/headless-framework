// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;

namespace Tests;

public sealed class NodeMembershipContractTests : TestBase
{
    [Fact]
    public async Task should_have_null_membership_with_empty_live_set_and_never_lost_token()
    {
        // given
        var sut = new NullNodeMembership();
        var identity = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));

        // when
        var liveNodes = await sut.GetLiveNodesAsync(AbortToken);
        var alive = await sut.IsAliveAsync(identity, AbortToken);

        // then
        liveNodes.Should().BeEmpty();
        alive.Should().BeFalse();
        sut.LocalMembershipLostToken.CanBeCanceled.Should().BeFalse();
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_cancel_null_event_stream_when_requested()
    {
        // given
        var sut = new NullNodeMembership();
        using var cts = new CancellationTokenSource();

        // when
        var enumerator = sut.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await cts.CancelAsync();
        var action = async () => await enumerator.MoveNextAsync();

        // then
        await action.Should().ThrowAsync<OperationCanceledException>();
        await enumerator.DisposeAsync();
    }

    [Fact]
    public void should_expose_all_membership_event_identities()
    {
        // given
        var identity = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(7));
        NodeMembershipEvent[] events =
        [
            new NodeJoined(identity),
            new NodeSuspected(identity),
            new NodeRecovered(identity),
            new NodeLeft(identity),
            new LocalMembershipLost(identity),
        ];

        // then
        events.Should().AllSatisfy(@event => @event.Identity.Should().Be(identity));
        events.Should().OnlyHaveUniqueItems(static @event => @event.GetType());
    }

    [Fact]
    public void should_default_membership_lost_behavior_to_stop_application()
    {
        // then
        ((int)MembershipLostBehavior.StopApplication)
            .Should()
            .Be(0);
        ((int)MembershipLostBehavior.StopMembershipOnly).Should().Be(1);
        new CoordinationOptions().MembershipLostBehavior.Should().Be(MembershipLostBehavior.StopApplication);
    }
}
