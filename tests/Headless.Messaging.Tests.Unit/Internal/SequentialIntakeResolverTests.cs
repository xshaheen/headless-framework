// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Internal;

public sealed class SequentialIntakeResolverTests : TestBase
{
    private const string _Orders = "orders";
    private const string _Invoices = "invoices";

    [Fact]
    public void should_leave_a_consumer_that_is_not_sequential_alone()
    {
        var intake = SequentialIntakeResolver.Resolve(
            MessageLane.Queue,
            sequential: false,
            [_Orders],
            _Transport(singleActiveConsumer: true),
            distributedLockConfigured: true
        );

        intake.Should().Be(SequentialIntake.NotSequential);
    }

    [Fact]
    public void should_trust_the_key_route_when_every_message_has_one_on_the_lane()
    {
        var intake = SequentialIntakeResolver.Resolve(
            MessageLane.Queue,
            sequential: true,
            [_Orders],
            _Transport(routes: [_Route(MessageLane.Queue, _Orders)]),
            distributedLockConfigured: true
        );

        intake.Should().Be(SequentialIntake.KeyRouted);
    }

    [Theory]
    [InlineData(MessageLane.Bus, _Orders)]
    [InlineData(MessageLane.Queue, _Invoices)]
    public void should_not_trust_a_key_route_that_misses_a_message_or_its_lane(MessageLane routeLane, string routed)
    {
        var intake = SequentialIntakeResolver.Resolve(
            MessageLane.Queue,
            sequential: true,
            [_Orders],
            _Transport(routes: [_Route(routeLane, routed)]),
            distributedLockConfigured: true
        );

        intake.Should().Be(SequentialIntake.DistributedLock);
    }

    [Fact]
    public void should_use_the_broker_single_active_consumer_when_no_key_route_covers_the_subscription()
    {
        var intake = SequentialIntakeResolver.Resolve(
            MessageLane.Queue,
            sequential: true,
            [_Orders, _Invoices],
            _Transport(singleActiveConsumer: true, routes: [_Route(MessageLane.Queue, _Orders)]),
            distributedLockConfigured: true
        );

        intake.Should().Be(SequentialIntake.BrokerSingleActiveConsumer);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_fall_back_to_the_lock_or_to_every_host_when_the_transport_offers_neither(bool lockConfigured)
    {
        var intake = SequentialIntakeResolver.Resolve(
            MessageLane.Bus,
            sequential: true,
            [_Orders],
            _Transport(),
            lockConfigured
        );

        intake.Should().Be(lockConfigured ? SequentialIntake.DistributedLock : SequentialIntake.Unguarded);
    }

    [Fact]
    public void should_treat_only_a_real_lock_provider_as_configured()
    {
        SequentialIntakeResolver
            .IsDistributedLockConfigured(new NullDistributedLock(new FakeTimeProvider(), logger: null))
            .Should()
            .BeFalse();
        SequentialIntakeResolver.IsDistributedLockConfigured(Substitute.For<IDistributedLock>()).Should().BeTrue();
    }

    private static MessagingProviderCapabilities _Transport(
        bool singleActiveConsumer = false,
        IReadOnlyCollection<MessagingRoutingAffinityRoute>? routes = null
    )
    {
        return MessagingProviderCapabilities.Transport(
            "Test",
            [MessageLane.Bus, MessageLane.Queue],
            supportsIndependentLaneTopology: true,
            routes,
            supportsSingleActiveConsumer: singleActiveConsumer
        );
    }

    private static MessagingRoutingAffinityRoute _Route(MessageLane lane, string messageName) =>
        new(lane, messageName, new MessagingRoutingAffinityMapping("native-key"));
}
