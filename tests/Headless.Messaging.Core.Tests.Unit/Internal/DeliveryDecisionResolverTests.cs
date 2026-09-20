// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

    // Every resolving cell of the (DeliveryMode x requireCoordination x coordination state) matrix, for both
    // lanes:
    // | Mode    | Coordination required | Compatible unit | No unit    | Incompatible unit |
    // | Durable | no                    | coordinated     | standalone | throw             |
    // | Durable | yes                   | coordinated     | throw      | throw             |
    // | Direct  | no                    | direct          | direct     | direct            |
    // | Direct  | yes                   | throw           | throw      | throw             |
    public static TheoryData<MessageLane, DeliveryMode, bool, int, int> ResolvingCells
    {
        get
        {
            var data = new TheoryData<MessageLane, DeliveryMode, bool, int, int>();
            foreach (var lane in new[] { MessageLane.Bus, MessageLane.Queue })
            {
                data.Add(lane, DeliveryMode.Durable, false, _None, _StandalonePath);
                data.Add(lane, DeliveryMode.Durable, false, _Compatible, _CoordinatedPath);
                data.Add(lane, DeliveryMode.Durable, true, _Compatible, _CoordinatedPath);
                data.Add(lane, DeliveryMode.Direct, false, _None, _DirectPath);
                data.Add(lane, DeliveryMode.Direct, false, _Compatible, _DirectPath);
                data.Add(lane, DeliveryMode.Direct, false, _Incompatible, _DirectPath);
            }

            return data;
        }
    }

    // Every rejecting cell of the matrix, for both lanes.
    public static TheoryData<MessageLane, DeliveryMode, bool, int, string> RejectingCells
    {
        get
        {
            var data = new TheoryData<MessageLane, DeliveryMode, bool, int, string>();
            foreach (var lane in new[] { MessageLane.Bus, MessageLane.Queue })
            {
                data.Add(lane, DeliveryMode.Durable, false, _Incompatible, "*cannot join the active unit of work*");
                data.Add(lane, DeliveryMode.Durable, true, _Incompatible, "*cannot join the active unit of work*");
                data.Add(
                    lane,
                    DeliveryMode.Durable,
                    true,
                    _None,
                    "*requires an active unit of work*none was supplied*"
                );
                data.Add(lane, DeliveryMode.Direct, true, _None, "*Direct delivery cannot be coordinated*");
                data.Add(lane, DeliveryMode.Direct, true, _Compatible, "*Direct delivery cannot be coordinated*");
                data.Add(lane, DeliveryMode.Direct, true, _Incompatible, "*Direct delivery cannot be coordinated*");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ResolvingCells))]
    public void should_resolve_every_resolving_cell_of_the_delivery_matrix(
        MessageLane lane,
        DeliveryMode requestedMode,
        bool requireCoordination,
        int status,
        int path
    )
    {
        var coordination = _Coordination(status);

        var decision = DeliveryDecisionResolver.Resolve(
            lane,
            requestedMode,
            requireCoordination,
            delay: null,
            coordination,
            _Now
        );

        decision.RequestedMode.Should().Be(requestedMode);
        decision.ResolvedMode.Should().Be(requestedMode);
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
        bool requireCoordination,
        int status,
        string messagePattern
    )
    {
        var coordination = _Coordination(status);

        var act = () =>
            DeliveryDecisionResolver.Resolve(lane, requestedMode, requireCoordination, delay: null, coordination, _Now);

        act.Should().Throw<InvalidOperationException>().WithMessage(messagePattern);
    }

    [Fact]
    public void should_name_the_message_type_when_coordination_is_required_and_no_unit_of_work_was_supplied()
    {
        // Acceptance Example B of the unit-of-work plan: an operator reading the failure must see which message
        // type asked for coordination, matching the Jobs sibling that names the function.
        var act = () =>
            DeliveryDecisionResolver.Resolve(
                MessageLane.Bus,
                DeliveryMode.Durable,
                requireCoordination: true,
                delay: null,
                _Coordination(_None),
                _Now,
                messageName: "OrderPlaced"
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("Publishing 'OrderPlaced' *");
    }

    [Theory]
    [InlineData(false, _None, _StandalonePath)]
    [InlineData(false, _Compatible, _CoordinatedPath)]
    [InlineData(true, _Compatible, _CoordinatedPath)]
    public void should_preserve_a_delay_on_every_durable_path(bool requireCoordination, int status, int path)
    {
        var delay = TimeSpan.FromMinutes(1);

        var decision = DeliveryDecisionResolver.Resolve(
            MessageLane.Queue,
            DeliveryMode.Durable,
            requireCoordination,
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
    [InlineData(true, _None)]
    [InlineData(true, _Incompatible)]
    [InlineData(false, _Incompatible)]
    public void should_reject_a_delayed_publish_on_a_rejecting_cell(bool requireCoordination, int status)
    {
        var act = () =>
            DeliveryDecisionResolver.Resolve(
                MessageLane.Bus,
                DeliveryMode.Durable,
                requireCoordination,
                TimeSpan.FromMinutes(1),
                _Coordination(status),
                _Now
            );

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false, _None)]
    [InlineData(false, _Compatible)]
    [InlineData(true, _Compatible)]
    public void should_reject_durable_delivery_when_the_lane_has_no_storage_support(
        bool requireCoordination,
        int status
    )
    {
        var act = () =>
            DeliveryDecisionResolver.Resolve(
                MessageLane.Bus,
                DeliveryMode.Durable,
                requireCoordination,
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
            requireCoordination: false,
            delay: null,
            _Coordination(status),
            _Now,
            storageSupported: false
        );

        decision.ResolvedMode.Should().Be(DeliveryMode.Direct);
        decision.Path.Should().Be(DeliveryPath.Direct);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_report_the_mismatch_reason_for_an_incompatible_unit(bool requireCoordination)
    {
        var coordination = DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.Database);

        var act = () =>
            DeliveryDecisionResolver.Resolve(
                MessageLane.Bus,
                DeliveryMode.Durable,
                requireCoordination,
                null,
                coordination,
                _Now
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*Database*");
    }

    [Fact]
    public void should_name_the_missing_relational_resource_instead_of_advising_a_begin_when_the_unit_has_none()
    {
        // The defect this closes: a developer who already called BeginAsync was told to call BeginAsync. The unit
        // exists — it simply exposes no relational resource for the messaging storage to write into.
        var coordination = DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.MissingRelationalCapability);

        var act = () =>
            DeliveryDecisionResolver.Resolve(
                MessageLane.Bus,
                DeliveryMode.Durable,
                requireCoordination: true,
                delay: null,
                coordination,
                _Now,
                messageName: "OrderPlaced"
            );

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("relational resource");
        message.Should().NotContain("BeginAsync");
    }

    [Fact]
    public void should_reject_an_undefined_delivery_mode()
    {
        var act = () =>
            DeliveryDecisionResolver.Resolve(
                MessageLane.Bus,
                (DeliveryMode)99,
                requireCoordination: false,
                null,
                DeliveryCoordination.None,
                _Now
            );

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
                requireCoordination: false,
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
                requireCoordination: false,
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
                requireCoordination: false,
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
            requireCoordination: false,
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
            requireCoordination: false,
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
                requireCoordination: false,
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
            requireCoordination: false,
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
            requireCoordination: false,
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
                requireCoordination: false,
                delay: null,
                DeliveryCoordination.None,
                _Now,
                scheduledAt: _Now.AddHours(1)
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*Direct*schedule*");
    }

    [Fact]
    public void should_schedule_coordinated_delivery_inside_a_compatible_unit()
    {
        var coordination = _CompatibleCoordination();

        var decision = DeliveryDecisionResolver.Resolve(
            MessageLane.Bus,
            DeliveryMode.Durable,
            requireCoordination: true,
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
#pragma warning disable CA2000 // The returned coordination carries the unit of work.
        return DeliveryCoordination.Compatible(FakeUnitOfWorks.CreateActive(), transaction: null);
#pragma warning restore CA2000
    }
}
