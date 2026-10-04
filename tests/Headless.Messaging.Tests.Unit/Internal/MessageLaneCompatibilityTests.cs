// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;

namespace Tests.Internal;

public sealed class MessageLaneCompatibilityTests : TestBase
{
    [Theory]
    [InlineData(0, MessageLane.Bus)]
    [InlineData(1, MessageLane.Queue)]
    public void should_map_persisted_value_to_lane(short persistedValue, MessageLane lane)
    {
        MessageLaneCompatibility.FromPersistedValue(persistedValue).Should().Be(lane);
    }

    [Theory]
    [InlineData(MessageLane.Bus, 0)]
    [InlineData(MessageLane.Queue, 1)]
    public void should_map_lane_to_persisted_value(MessageLane lane, short persistedValue)
    {
        MessageLaneCompatibility.ToPersistedValue(lane).Should().Be(persistedValue);
    }

    [Theory]
    [InlineData(short.MinValue)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(42)]
    [InlineData(short.MaxValue)]
    public void should_reject_every_representative_unknown_legacy_intent_without_defaulting_to_bus(short value)
    {
        var act = () => MessageLaneCompatibility.FromPersistedValue(value);

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
        var act = () => MessageLaneCompatibility.ToPersistedValue((MessageLane)value);

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
