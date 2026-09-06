// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.CommitCoordination;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;

namespace Tests.Internal;

public sealed class DeliveryDecisionResolverTests : TestBase
{
    private static readonly DateTimeOffset _Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<DeliveryMode, bool, TimeSpan?, DeliveryMode, int, bool> ValidDecisions =>
        new()
        {
            { DeliveryMode.Auto, false, null, DeliveryMode.Direct, (int)DeliveryPath.Direct, false },
            { DeliveryMode.Auto, true, null, DeliveryMode.Durable, (int)DeliveryPath.DurableCoordinated, false },
            {
                DeliveryMode.Auto,
                false,
                TimeSpan.FromMinutes(1),
                DeliveryMode.Durable,
                (int)DeliveryPath.DurableStandalone,
                true
            },
            {
                DeliveryMode.Auto,
                true,
                TimeSpan.FromMinutes(1),
                DeliveryMode.Durable,
                (int)DeliveryPath.DurableCoordinated,
                true
            },
            { DeliveryMode.Durable, false, null, DeliveryMode.Durable, (int)DeliveryPath.DurableStandalone, false },
            { DeliveryMode.Durable, true, null, DeliveryMode.Durable, (int)DeliveryPath.DurableCoordinated, false },
            { DeliveryMode.Direct, false, null, DeliveryMode.Direct, (int)DeliveryPath.Direct, false },
            { DeliveryMode.Direct, true, null, DeliveryMode.Direct, (int)DeliveryPath.Direct, false },
        };

    [Theory]
    [MemberData(nameof(ValidDecisions))]
    public void should_resolve_the_delivery_table_once(
        DeliveryMode requestedMode,
        bool compatibleCoordination,
        TimeSpan? delay,
        DeliveryMode resolvedMode,
        int path,
        bool scheduled
    )
    {
        var coordination = compatibleCoordination ? _CompatibleCoordination() : DeliveryCoordination.None;

        var decision = DeliveryDecisionResolver.Resolve(MessageLane.Bus, requestedMode, delay, coordination, _Now);

        decision.RequestedMode.Should().Be(requestedMode);
        decision.ResolvedMode.Should().Be(resolvedMode);
        decision.Path.Should().Be((DeliveryPath)path);
        decision.Delay.Should().Be(delay);
        decision.PublishAt.Should().Be(scheduled ? _Now + delay!.Value : null);
        decision.Coordination.Should().Be(coordination);
    }

    [Fact]
    public void should_reject_an_undefined_delivery_mode()
    {
        var act = () =>
            DeliveryDecisionResolver.Resolve(MessageLane.Bus, (DeliveryMode)99, null, DeliveryCoordination.None, _Now);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("requestedMode");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_non_positive_delay_before_resolution(int ticks)
    {
        var act = () =>
            DeliveryDecisionResolver.Resolve(
                MessageLane.Queue,
                DeliveryMode.Auto,
                TimeSpan.FromTicks(ticks),
                DeliveryCoordination.None,
                _Now
            );

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("delay");
    }

    [Fact]
    public void should_reject_a_delay_that_overflows_the_clock()
    {
        var act = () =>
            DeliveryDecisionResolver.Resolve(
                MessageLane.Queue,
                DeliveryMode.Auto,
                TimeSpan.MaxValue,
                DeliveryCoordination.None,
                _Now
            );

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("delay");
    }

    [Fact]
    public void should_reject_delayed_transport_direct()
    {
        var act = () =>
            DeliveryDecisionResolver.Resolve(
                MessageLane.Bus,
                DeliveryMode.Direct,
                TimeSpan.FromSeconds(1),
                DeliveryCoordination.None,
                _Now
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*Direct*delay*");
    }

    [Theory]
    [InlineData(DeliveryMode.Auto)]
    [InlineData(DeliveryMode.Durable)]
    public void should_reject_incompatible_coordination(DeliveryMode mode)
    {
        var coordination = DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.StorageProvider);
        var act = () => DeliveryDecisionResolver.Resolve(MessageLane.Bus, mode, null, coordination, _Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("*coordination*StorageProvider*");
    }

    [Fact]
    public void should_bypass_incompatible_coordination_for_transport_direct()
    {
        var coordination = DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.StorageProvider);

        var decision = DeliveryDecisionResolver.Resolve(MessageLane.Bus, DeliveryMode.Direct, null, coordination, _Now);

        decision.ResolvedMode.Should().Be(DeliveryMode.Direct);
        decision.Path.Should().Be(DeliveryPath.Direct);
    }

    [Fact]
    public void should_resolve_publish_at_from_an_absolute_schedule()
    {
        var scheduledAt = _Now.AddHours(3);

        var decision = DeliveryDecisionResolver.Resolve(
            MessageLane.Bus,
            DeliveryMode.Durable,
            delay: null,
            DeliveryCoordination.None,
            _Now,
            scheduledAt: scheduledAt
        );

        decision.PublishAt.Should().Be(scheduledAt);
        decision.ScheduledAt.Should().Be(scheduledAt);
        decision.Delay.Should().BeNull();
    }

    [Fact]
    public void should_reject_supplying_both_a_delay_and_an_absolute_schedule()
    {
        var act = () =>
            DeliveryDecisionResolver.Resolve(
                MessageLane.Bus,
                DeliveryMode.Durable,
                TimeSpan.FromMinutes(5),
                DeliveryCoordination.None,
                _Now,
                scheduledAt: _Now.AddHours(1)
            );

        act.Should().Throw<ArgumentException>().WithMessage("*Delay*ScheduledAt*");
    }

    [Fact]
    public void should_accept_an_absolute_schedule_already_in_the_past()
    {
        var scheduledAt = _Now.AddHours(-1);

        var decision = DeliveryDecisionResolver.Resolve(
            MessageLane.Bus,
            DeliveryMode.Durable,
            delay: null,
            DeliveryCoordination.None,
            _Now,
            scheduledAt: scheduledAt
        );

        decision.PublishAt.Should().Be(scheduledAt);
    }

    [Fact]
    public void should_normalize_a_non_utc_absolute_schedule_to_the_same_instant()
    {
        var scheduledAt = new DateTimeOffset(2026, 7, 26, 17, 0, 0, TimeSpan.FromHours(3));

        var decision = DeliveryDecisionResolver.Resolve(
            MessageLane.Bus,
            DeliveryMode.Durable,
            delay: null,
            DeliveryCoordination.None,
            _Now,
            scheduledAt: scheduledAt
        );

        decision.PublishAt!.Value.Offset.Should().Be(TimeSpan.Zero);
        decision.PublishAt!.Value.ToUniversalTime().Should().Be(scheduledAt.ToUniversalTime());
    }

    [Fact]
    public void should_reject_an_absolute_schedule_on_direct_delivery()
    {
        var act = () =>
            DeliveryDecisionResolver.Resolve(
                MessageLane.Bus,
                DeliveryMode.Direct,
                delay: null,
                DeliveryCoordination.None,
                _Now,
                scheduledAt: _Now.AddHours(1)
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*Direct*schedule*");
    }

    [Fact]
    public void should_upgrade_auto_to_durable_for_an_absolute_schedule()
    {
        var decision = DeliveryDecisionResolver.Resolve(
            MessageLane.Bus,
            DeliveryMode.Auto,
            delay: null,
            DeliveryCoordination.None,
            _Now,
            scheduledAt: _Now.AddHours(1)
        );

        decision.ResolvedMode.Should().Be(DeliveryMode.Durable);
        decision.Path.Should().Be(DeliveryPath.DurableStandalone);
    }

    private static DeliveryCoordination _CompatibleCoordination()
    {
        return DeliveryCoordination.Compatible(Substitute.For<ICommitCoordinator>(), Substitute.For<DbTransaction>());
    }
}
