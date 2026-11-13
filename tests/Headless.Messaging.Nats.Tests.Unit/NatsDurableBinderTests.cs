// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Tests;

public sealed class NatsDurableBinderTests : TestBase
{
    private const string _Stream = "headless-bus-orders";
    private const string _Durable = "bus_billing";
    private const string _LegacyCreated = "bus-billing-orders-created";
    private const string _LegacyUpdated = "bus-billing-orders-updated";

    private readonly INatsJSContext _js = Substitute.For<INatsJSContext>();
    private readonly List<string> _warnings = [];

    public NatsDurableBinderTests()
    {
        _js.GetConsumerAsync(_Stream, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<INatsJSConsumer>>(_ => throw _NotFound());
        _js.GetStreamAsync(_Stream, Arg.Any<StreamInfoRequest?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var stream = Substitute.For<INatsJSStream>();
                stream.Info.Returns(new StreamInfo { State = new StreamState { LastSeq = 900 } });
                return new ValueTask<INatsJSStream>(stream);
            });
        _js.CreateConsumerAsync(_Stream, Arg.Any<ConsumerConfig>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<INatsJSConsumer>(_Consumer(call.Arg<ConsumerConfig>())));
        _js.CreateOrUpdateConsumerAsync(_Stream, Arg.Any<ConsumerConfig>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<INatsJSConsumer>(_Consumer(call.Arg<ConsumerConfig>())));
    }

    #region MergeFilters

    [Fact]
    public void should_keep_disjoint_filters_in_order()
    {
        NatsDurableBinder
            .MergeFilters(["headless.bus.orders.created", "headless.bus.orders.created.>", "headless.bus.orders.paid"])
            .Should()
            .Equal("headless.bus.orders.created", "headless.bus.orders.created.>", "headless.bus.orders.paid");
    }

    [Fact]
    public void should_drop_a_filter_another_filter_covers()
    {
        NatsDurableBinder
            .MergeFilters(["acme.*.runs.*.result", "acme.t1.runs.r1.result", "acme.*.runs.*.result"])
            .Should()
            .Equal("acme.*.runs.*.result");
    }

    [Fact]
    public void should_refuse_filters_that_overlap_without_one_covering_the_other()
    {
        var act = () => NatsDurableBinder.MergeFilters(["acme.*.runs", "acme.t1.*"]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'acme.*.runs'*'acme.t1.*'*overlap*");
    }

    #endregion

    #region HandOffSequenceAsync

    [Fact]
    public async Task should_not_hand_off_when_no_legacy_durable_exists()
    {
        var start = await NatsDurableBinder.HandOffSequenceAsync(_js, _Stream, [_LegacyCreated], AbortToken);

        start.Should().BeNull();
    }

    [Fact]
    public async Task should_start_after_the_lowest_acknowledgement_floor_of_a_legacy_durable_with_pending_messages()
    {
        // given - "created" still has messages to deliver; "updated" stopped further on
        _Legacy(_LegacyCreated, ackFloor: 120, numPending: 3, numAckPending: 0);
        _Legacy(_LegacyUpdated, ackFloor: 400, numPending: 0, numAckPending: 2);

        // when
        var start = await NatsDurableBinder.HandOffSequenceAsync(
            _js,
            _Stream,
            [_LegacyCreated, _LegacyUpdated],
            AbortToken
        );

        // then
        start.Should().Be(121UL);
    }

    [Fact]
    public async Task should_treat_a_caught_up_legacy_durable_as_stopped_at_the_stream_end()
    {
        // given - a rarely published subject's ack floor stays far behind the stream although nothing is pending
        _Legacy(_LegacyCreated, ackFloor: 5, numPending: 0, numAckPending: 0);
        _Legacy(_LegacyUpdated, ackFloor: 890, numPending: 0, numAckPending: 0);

        // when
        var start = await NatsDurableBinder.HandOffSequenceAsync(
            _js,
            _Stream,
            [_LegacyCreated, _LegacyUpdated],
            AbortToken
        );

        // then - no replay of the busy subject's history
        start.Should().Be(901UL);
    }

    #endregion

    #region BindAsync

    [Fact]
    public async Task should_create_or_update_a_queue_durable_as_configured()
    {
        var config = new ConsumerConfig("queue-orders") { FilterSubject = "headless.queue.orders" };

        await NatsDurableBinder.BindAsync(_js, _Stream, config, [], _warnings.Add, AbortToken);

        await _js.Received(1).CreateOrUpdateConsumerAsync(_Stream, config, Arg.Any<CancellationToken>());
        await _js.DidNotReceiveWithAnyArgs().CreateConsumerAsync(default!, default!, AbortToken);
    }

    [Fact]
    public async Task should_create_a_new_bus_durable_at_the_hand_off_sequence_and_delete_the_legacy_durables()
    {
        // given
        _Legacy(_LegacyCreated, ackFloor: 120, numPending: 3, numAckPending: 0);

        // when
        await NatsDurableBinder.BindAsync(
            _js,
            _Stream,
            _BusConfig(),
            [_LegacyCreated, _LegacyUpdated],
            _warnings.Add,
            AbortToken
        );

        // then
        await _js.Received(1)
            .CreateConsumerAsync(
                _Stream,
                Arg.Is<ConsumerConfig>(c =>
                    c.DeliverPolicy == ConsumerConfigDeliverPolicy.ByStartSequence && c.OptStartSeq == 121
                ),
                Arg.Any<CancellationToken>()
            );
        await _js.Received(1).DeleteConsumerAsync(_Stream, _LegacyCreated, Arg.Any<CancellationToken>());
        await _js.Received(1).DeleteConsumerAsync(_Stream, _LegacyUpdated, Arg.Any<CancellationToken>());
        _warnings.Should().ContainSingle().Which.Should().Contain("stream sequence 121");
    }

    [Fact]
    public async Task should_create_a_new_bus_durable_at_new_messages_when_nothing_to_hand_off()
    {
        await NatsDurableBinder.BindAsync(_js, _Stream, _BusConfig(), [_LegacyCreated], _warnings.Add, AbortToken);

        await _js.Received(1)
            .CreateConsumerAsync(
                _Stream,
                Arg.Is<ConsumerConfig>(c => c.DeliverPolicy == ConsumerConfigDeliverPolicy.New),
                Arg.Any<CancellationToken>()
            );
        _warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task should_keep_the_start_position_of_an_existing_bus_durable()
    {
        // given - the durable was created by a hand-off, so it starts at a sequence
        var existing = _Consumer(
            _BusConfig() with
            {
                DeliverPolicy = ConsumerConfigDeliverPolicy.ByStartSequence,
                OptStartSeq = 77,
            }
        );
        _js.GetConsumerAsync(_Stream, _Durable, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<INatsJSConsumer>(existing));

        // when
        await NatsDurableBinder.BindAsync(_js, _Stream, _BusConfig(), [_LegacyCreated], _warnings.Add, AbortToken);

        // then - updated in place without the start change JetStream refuses, and no second hand-off
        await _js.Received(1)
            .CreateOrUpdateConsumerAsync(
                _Stream,
                Arg.Is<ConsumerConfig>(c =>
                    c.DeliverPolicy == ConsumerConfigDeliverPolicy.ByStartSequence && c.OptStartSeq == 77
                ),
                Arg.Any<CancellationToken>()
            );
        await _js.DidNotReceiveWithAnyArgs().CreateConsumerAsync(default!, default!, AbortToken);
        await _js.DidNotReceiveWithAnyArgs().GetStreamAsync(default!, default, AbortToken);
        _warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task should_delete_legacy_durables_left_behind_when_binding_an_existing_bus_durable()
    {
        // given - an earlier hand-off created the durable but stopped before its deletes, or an old replica recreated a
        // legacy durable during a rolling deploy
        var existing = _Consumer(_BusConfig());
        _js.GetConsumerAsync(_Stream, _Durable, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<INatsJSConsumer>(existing));
        _Legacy(_LegacyUpdated, ackFloor: 3, numPending: 5, numAckPending: 0);

        // when
        await NatsDurableBinder.BindAsync(
            _js,
            _Stream,
            _BusConfig(),
            [_LegacyCreated, _LegacyUpdated],
            _warnings.Add,
            AbortToken
        );

        // then - both are deleted, and the existing durable keeps its own start
        await _js.Received(1).DeleteConsumerAsync(_Stream, _LegacyCreated, Arg.Any<CancellationToken>());
        await _js.Received(1).DeleteConsumerAsync(_Stream, _LegacyUpdated, Arg.Any<CancellationToken>());
        await _js.DidNotReceiveWithAnyArgs().CreateConsumerAsync(default!, default!, AbortToken);
        _warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task should_bind_the_durable_another_client_created_first()
    {
        // given - the create loses a race, then the lookup finds the winner's durable
        var lookups = 0;
        _js.GetConsumerAsync(_Stream, _Durable, Arg.Any<CancellationToken>())
            .Returns(_ =>
                Interlocked.Increment(ref lookups) == 1
                    ? throw _NotFound()
                    : new ValueTask<INatsJSConsumer>(_Consumer(_BusConfig()))
            );
        _js.CreateConsumerAsync(_Stream, Arg.Any<ConsumerConfig>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<INatsJSConsumer>>(_ =>
                throw new NatsJSApiException(new ApiError { Code = 400, ErrCode = 10148 })
            );

        // when
        await NatsDurableBinder.BindAsync(_js, _Stream, _BusConfig(), [_LegacyCreated], _warnings.Add, AbortToken);

        // then
        await _js.Received(1)
            .CreateOrUpdateConsumerAsync(_Stream, Arg.Any<ConsumerConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_warn_and_continue_when_a_legacy_durable_cannot_be_deleted()
    {
        // given
        _Legacy(_LegacyCreated, ackFloor: 10, numPending: 1, numAckPending: 0);
        _js.DeleteConsumerAsync(_Stream, _LegacyCreated, Arg.Any<CancellationToken>())
            .Returns<ValueTask<bool>>(_ =>
                throw new NatsJSApiException(new ApiError { Code = 403, Description = "permissions violation" })
            );

        // when
        var consumer = await NatsDurableBinder.BindAsync(
            _js,
            _Stream,
            _BusConfig(),
            [_LegacyCreated],
            _warnings.Add,
            AbortToken
        );

        // then
        consumer.Should().NotBeNull();
        _warnings.Should().Contain(w => w.Contains($"nats consumer rm {_Stream} {_LegacyCreated}"));
    }

    [Fact]
    public async Task should_fail_when_the_server_drops_the_filter_subjects()
    {
        // given - a server before 2.10 echoes no FilterSubjects
        _js.CreateConsumerAsync(_Stream, Arg.Any<ConsumerConfig>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<INatsJSConsumer>(
                _Consumer(call.Arg<ConsumerConfig>() with { FilterSubjects = null })
            ));

        // when
        var act = () =>
            NatsDurableBinder.BindAsync(_js, _Stream, _BusConfig(), [_LegacyCreated], _warnings.Add, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*2.10*");
    }

    #endregion

    private static ConsumerConfig _BusConfig() =>
        new(_Durable)
        {
            DeliverPolicy = ConsumerConfigDeliverPolicy.New,
            FilterSubjects = ["headless.bus.orders.created", "headless.bus.orders.updated"],
        };

    private void _Legacy(string durable, ulong ackFloor, ulong numPending, int numAckPending)
    {
        var consumer = Substitute.For<INatsJSConsumer>();
        consumer.Info.Returns(
            new ConsumerInfo
            {
                StreamName = _Stream,
                Name = durable,
                Config = new ConsumerConfig(durable),
                AckFloor = new SequenceInfo { StreamSeq = ackFloor },
                NumPending = numPending,
                NumAckPending = numAckPending,
            }
        );
        _js.GetConsumerAsync(_Stream, durable, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<INatsJSConsumer>(consumer));
    }

    private static INatsJSConsumer _Consumer(ConsumerConfig config)
    {
        var consumer = Substitute.For<INatsJSConsumer>();
        consumer.Info.Returns(
            new ConsumerInfo
            {
                StreamName = _Stream,
                Name = config.Name!,
                Config = config,
            }
        );
        return consumer;
    }

    private static NatsJSApiException _NotFound() =>
        new(
            new ApiError
            {
                Code = 404,
                ErrCode = 10014,
                Description = "consumer not found",
            }
        );
}
