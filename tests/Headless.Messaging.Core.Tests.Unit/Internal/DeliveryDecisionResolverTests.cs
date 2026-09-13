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

    // DeliveryCoordinationStatus and DeliveryPath are internal, so theory rows carry them as int.
    private const int _None = (int)DeliveryCoordinationStatus.None;
    private const int _Compatible = (int)DeliveryCoordinationStatus.Compatible;
    private const int _Incompatible = (int)DeliveryCoordinationStatus.Incompatible;
    private const int _DirectPath = (int)DeliveryPath.Direct;
    private const int _StandalonePath = (int)DeliveryPath.DurableStandalone;
    private const int _CoordinatedPath = (int)DeliveryPath.DurableCoordinated;

    // Every resolving cell of the delivery matrix, for both lanes:
    // | Requested   | Compatible scope      | No scope           | Incompatible scope |
    // | Durable     | coordinated durable   | standalone durable | throw              |
    // | Coordinated | coordinated durable   | throw              | throw              |
    // | Direct      | direct                | direct             | direct             |
    public static TheoryData<MessageLane, DeliveryMode, int, DeliveryMode, int> ResolvingCells
    {
        get
        {
            var data = new TheoryData<MessageLane, DeliveryMode, int, DeliveryMode, int>();
            foreach (var lane in new[] { MessageLane.Bus, MessageLane.Queue })
            {
                data.Add(lane, DeliveryMode.Durable, _None, DeliveryMode.Durable, _StandalonePath);
                data.Add(lane, DeliveryMode.Durable, _Compatible, DeliveryMode.Durable, _CoordinatedPath);
                data.Add(lane, DeliveryMode.Coordinated, _Compatible, DeliveryMode.Durable, _CoordinatedPath);
                data.Add(lane, DeliveryMode.Direct, _None, DeliveryMode.Direct, _DirectPath);
                data.Add(lane, DeliveryMode.Direct, _Compatible, DeliveryMode.Direct, _DirectPath);
                data.Add(lane, DeliveryMode.Direct, _Incompatible, DeliveryMode.Direct, _DirectPath);
            }

            return data;
        }
    }

    // Every rejecting cell of the matrix, for both lanes.
    public static TheoryData<MessageLane, DeliveryMode, int, string> RejectingCells
    {
        get
        {
            var data = new TheoryData<MessageLane, DeliveryMode, int, string>();
            foreach (var lane in new[] { MessageLane.Bus, MessageLane.Queue })
            {
                data.Add(lane, DeliveryMode.Durable, _Incompatible, "*incompatible*");
                data.Add(lane, DeliveryMode.Coordinated, _None, "*Coordinated*no scope is active*");
                data.Add(lane, DeliveryMode.Coordinated, _Incompatible, "*incompatible*");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ResolvingCells))]
    public void should_resolve_every_resolving_cell_of_the_delivery_matrix(
        MessageLane lane,
        DeliveryMode requestedMode,
        int status,
        DeliveryMode resolvedMode,
        int path
    )
    {
        var coordination = _Coordination(status);

        var decision = DeliveryDecisionResolver.Resolve(lane, requestedMode, delay: null, coordination, _Now);

        decision.RequestedMode.Should().Be(requestedMode);
        decision.ResolvedMode.Should().Be(resolvedMode);
        decision.Path.Should().Be((DeliveryPath)path);
        decision.IsTransactional.Should().Be(path == _CoordinatedPath);
        decision.Delay.Should().BeNull();
        decision.PublishAt.Should().BeNull();
        decision.Coordination.Should().Be(coordination);
    }

    [Theory]
    [MemberData(nameof(RejectingCells))]
    public void should_reject_every_rejecting_cell_of_the_delivery_matrix(
        MessageLane lane,
        DeliveryMode requestedMode,
        int status,
        string messagePattern
    )
    {
        var coordination = _Coordination(status);

        var act = () => DeliveryDecisionResolver.Resolve(lane, requestedMode, delay: null, coordination, _Now);

        act.Should().Throw<InvalidOperationException>().WithMessage(messagePattern);
    }

    [Theory]
    [InlineData(DeliveryMode.Durable, _None, _StandalonePath)]
    [InlineData(DeliveryMode.Durable, _Compatible, _CoordinatedPath)]
    [InlineData(DeliveryMode.Coordinated, _Compatible, _CoordinatedPath)]
    public void should_preserve_a_delay_on_every_durable_path(DeliveryMode requestedMode, int status, int path)
    {
        var delay = TimeSpan.FromMinutes(1);

        var decision = DeliveryDecisionResolver.Resolve(
            MessageLane.Queue,
            requestedMode,
            delay,
            _Coordination(status),
            _Now
        );

        decision.ResolvedMode.Should().Be(DeliveryMode.Durable);
        decision.Path.Should().Be((DeliveryPath)path);
        decision.Delay.Should().Be(delay);
        decision.PublishAt.Should().Be(_Now + delay);
    }

    [Theory]
    [InlineData(DeliveryMode.Coordinated, _None)]
    [InlineData(DeliveryMode.Coordinated, _Incompatible)]
    [InlineData(DeliveryMode.Durable, _Incompatible)]
    public void should_reject_a_delayed_publish_on_a_rejecting_cell(DeliveryMode requestedMode, int status)
    {
        var act = () =>
            DeliveryDecisionResolver.Resolve(
                MessageLane.Bus,
                requestedMode,
                TimeSpan.FromMinutes(1),
                _Coordination(status),
                _Now
            );

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(DeliveryMode.Durable, _None)]
    [InlineData(DeliveryMode.Durable, _Compatible)]
    [InlineData(DeliveryMode.Coordinated, _Compatible)]
    public void should_reject_durable_delivery_when_the_lane_has_no_storage_support(
        DeliveryMode requestedMode,
        int status
    )
    {
        var act = () =>
            DeliveryDecisionResolver.Resolve(
                MessageLane.Bus,
                requestedMode,
                delay: null,
                _Coordination(status),
                _Now,
                storageSupported: false
            );

        act.Should().Throw<MessagingConfigurationException>().WithMessage("*Bus*storage*");
    }

    [Theory]
    [InlineData(_None)]
    [InlineData(_Compatible)]
    [InlineData(_Incompatible)]
    public void should_resolve_direct_delivery_without_storage_support(int status)
    {
        var decision = DeliveryDecisionResolver.Resolve(
            MessageLane.Queue,
            DeliveryMode.Direct,
            delay: null,
            _Coordination(status),
            _Now,
            storageSupported: false
        );

        decision.ResolvedMode.Should().Be(DeliveryMode.Direct);
        decision.Path.Should().Be(DeliveryPath.Direct);
    }

    [Theory]
    [InlineData(DeliveryMode.Durable)]
    [InlineData(DeliveryMode.Coordinated)]
    public void should_reject_a_compatible_boundary_whose_transaction_is_no_longer_live(DeliveryMode requestedMode)
    {
        var coordinator = Substitute.For<ICommitCoordinator>();
        coordinator.State.Returns(CommitCoordinatorState.Committed);
        var coordination = DeliveryCoordination.Compatible(coordinator, Substitute.For<DbTransaction>());

        var act = () => DeliveryDecisionResolver.Resolve(MessageLane.Bus, requestedMode, null, coordination, _Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("*no longer live*Committed*");
    }

    [Fact]
    public void should_send_direct_through_a_compatible_boundary_whose_transaction_is_no_longer_live()
    {
        var coordinator = Substitute.For<ICommitCoordinator>();
        coordinator.State.Returns(CommitCoordinatorState.RolledBack);
        var coordination = DeliveryCoordination.Compatible(coordinator, Substitute.For<DbTransaction>());

        var decision = DeliveryDecisionResolver.Resolve(MessageLane.Bus, DeliveryMode.Direct, null, coordination, _Now);

        decision.Path.Should().Be(DeliveryPath.Direct);
    }

    [Fact]
    public void should_report_the_mismatch_reason_for_an_incompatible_boundary()
    {
        var coordination = DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.InactiveTransaction);

        var act = () =>
            DeliveryDecisionResolver.Resolve(MessageLane.Bus, DeliveryMode.Coordinated, null, coordination, _Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("*InactiveTransaction*");
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
                DeliveryMode.Durable,
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
                DeliveryMode.Durable,
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
    public void should_normalize_absolute_eligibility_to_microseconds_without_changing_requested_instant()
    {
        var scheduledAt = _Now.AddTicks(17);
        var decision = DeliveryDecisionResolver.Resolve(
            MessageLane.Bus,
            DeliveryMode.Durable,
            null,
            DeliveryCoordination.None,
            _Now,
            scheduledAt
        );

        decision.PublishAt.Should().Be(_Now.AddTicks(10));
        decision.ScheduledAt.Should().Be(scheduledAt);
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
    public void should_schedule_coordinated_delivery_inside_a_compatible_scope()
    {
        var coordination = _CompatibleCoordination();

        var decision = DeliveryDecisionResolver.Resolve(
            MessageLane.Bus,
            DeliveryMode.Coordinated,
            delay: null,
            coordination,
            _Now,
            scheduledAt: _Now.AddHours(1)
        );

        decision.Path.Should().Be(DeliveryPath.DurableCoordinated);
        decision.PublishAt.Should().Be(_Now.AddHours(1));
    }

    private static DeliveryCoordination _Coordination(int status) =>
        (DeliveryCoordinationStatus)status switch
        {
            DeliveryCoordinationStatus.Compatible => _CompatibleCoordination(),
            DeliveryCoordinationStatus.Incompatible => DeliveryCoordination.Incompatible(
                DeliveryCoordinationMismatch.StorageProvider
            ),
            _ => DeliveryCoordination.None,
        };

    private static DeliveryCoordination _CompatibleCoordination()
    {
        return DeliveryCoordination.Compatible(Substitute.For<ICommitCoordinator>(), Substitute.For<DbTransaction>());
    }
}
