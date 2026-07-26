// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;

namespace Tests.Internal;

public sealed class MessageLaneCompatibilityTests : TestBase
{
    [Theory]
    [InlineData(IntentType.Bus, MessageLane.Bus)]
    [InlineData(IntentType.Queue, MessageLane.Queue)]
    public void should_map_legacy_intent_to_lane(IntentType intentType, MessageLane lane)
    {
        MessageLaneCompatibility.ToLane(intentType).Should().Be(lane);
    }

    [Theory]
    [InlineData(MessageLane.Bus, IntentType.Bus)]
    [InlineData(MessageLane.Queue, IntentType.Queue)]
    public void should_map_lane_to_legacy_intent(MessageLane lane, IntentType intentType)
    {
        MessageLaneCompatibility.ToIntentType(lane).Should().Be(intentType);
    }

    [Theory]
    [InlineData(short.MinValue)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(42)]
    [InlineData(short.MaxValue)]
    public void should_reject_every_representative_unknown_legacy_intent_without_defaulting_to_bus(short value)
    {
        var act = () => MessageLaneCompatibility.ToLane((IntentType)value);

        act.Should().ThrowExactly<InvalidOperationException>().WithMessage($"*'{value}'*");
    }

    [Theory]
    [InlineData(short.MinValue)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(42)]
    [InlineData(short.MaxValue)]
    public void should_reject_every_representative_unknown_lane_without_defaulting_to_bus(short value)
    {
        var act = () => MessageLaneCompatibility.ToIntentType((MessageLane)value);

        act.Should().ThrowExactly<InvalidOperationException>().WithMessage($"*'{value}'*");
    }

    [Fact]
    public void should_compare_route_keys_by_contract_name_and_lane()
    {
        var key = new MessageRouteKey(typeof(TestMessage), "orders.changed", MessageLane.Bus);

        key.Should().Be(new MessageRouteKey(typeof(TestMessage), "orders.changed", MessageLane.Bus));
        key.Should().NotBe(new MessageRouteKey(typeof(OtherMessage), "orders.changed", MessageLane.Bus));
        key.Should().NotBe(new MessageRouteKey(typeof(TestMessage), "orders.created", MessageLane.Bus));
        key.Should().NotBe(new MessageRouteKey(typeof(TestMessage), "ORDERS.CHANGED", MessageLane.Bus));
        key.Should().NotBe(new MessageRouteKey(typeof(TestMessage), "orders.changed", MessageLane.Queue));
    }

    private static class TestMessage;

    private static class OtherMessage;
}
