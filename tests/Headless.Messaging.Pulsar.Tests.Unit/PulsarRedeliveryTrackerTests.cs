// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class PulsarRedeliveryTrackerTests : TestBase
{
    private const string _Topic = "persistent://public/default/headless-queue-orders";

    private readonly FakeTimeProvider _time = new();
    private readonly FakePulsarReceiver _receiver = new(_Topic);
    private readonly ConcurrentQueue<PulsarDelivery> _deadLettered = new();
    private readonly ConcurrentQueue<Exception> _errors = new();
    private bool _deadLetterSucceeds = true;

    [Fact]
    public async Task should_redeliver_a_rejected_message_once_the_delay_passed()
    {
        // given
        var tracker = _Tracker();
        var delivery = _Delivery();
        tracker.OnRejected(delivery);

        // when
        _time.Advance(TimeSpan.FromMilliseconds(999));
        await tracker.RedeliverDueAsync(AbortToken);
        var redeliveredEarly = _receiver.Redelivered.Count;
        _time.Advance(TimeSpan.FromMilliseconds(1));
        await tracker.RedeliverDueAsync(AbortToken);

        // then
        redeliveredEarly.Should().Be(0, "the broker redelivers a rejected message only after the delay");
        _receiver.Redelivered.Should().ContainSingle().Which.Should().Be(delivery.MessageId);
        tracker.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_redeliver_a_message_left_unacknowledged_past_the_ack_timeout()
    {
        // given
        var tracker = _Tracker(ackTimeout: TimeSpan.FromSeconds(5));
        var delivery = _Delivery();
        tracker.OnReceived(delivery);

        // when
        _time.Advance(TimeSpan.FromSeconds(5));
        await tracker.RedeliverDueAsync(AbortToken);

        // then
        _receiver.Redelivered.Should().ContainSingle().Which.Should().Be(delivery.MessageId);
    }

    [Fact]
    public async Task should_not_redeliver_an_acknowledged_message()
    {
        // given
        var tracker = _Tracker(ackTimeout: TimeSpan.FromSeconds(5));
        var delivery = _Delivery();
        tracker.OnReceived(delivery);

        // when
        tracker.OnAcknowledged(delivery);
        _time.Advance(TimeSpan.FromSeconds(10));
        await tracker.RedeliverDueAsync(AbortToken);

        // then
        _receiver.Redelivered.Should().BeEmpty();
    }

    [Fact]
    public void should_track_nothing_without_an_ack_timeout()
    {
        // given
        var tracker = _Tracker();

        // when
        tracker.OnReceived(_Delivery());

        // then
        tracker.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_track_nothing_for_a_receiver_that_does_not_settle()
    {
        // given: a reader of a non-durable subscription keeps no acknowledgements
        var tracker = _Tracker(ackTimeout: TimeSpan.FromSeconds(5));
        await using var reader = new FakePulsarReceiver(_Topic, settles: false);
        var delivery = new PulsarDelivery(reader, PulsarMessages.Create(_Topic));

        // when
        tracker.OnReceived(delivery);
        tracker.OnRejected(delivery);

        // then
        tracker.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_dead_letter_a_message_whose_redeliveries_reached_the_limit()
    {
        // given
        var tracker = _Tracker(maxRedeliveryCount: 2);
        var exhausted = _Delivery(redeliveryCount: 2);
        var retried = _Delivery(redeliveryCount: 1, entryId: 23);
        tracker.OnRejected(exhausted);
        tracker.OnRejected(retried);

        // when
        _time.Advance(TimeSpan.FromSeconds(1));
        await tracker.RedeliverDueAsync(AbortToken);

        // then
        _deadLettered.Should().ContainSingle().Which.Should().BeSameAs(exhausted);
        _receiver.Redelivered.Should().ContainSingle().Which.Should().Be(retried.MessageId);
    }

    [Fact]
    public async Task should_redeliver_instead_when_the_dead_letter_publish_fails()
    {
        // given
        _deadLetterSucceeds = false;
        var tracker = _Tracker(maxRedeliveryCount: 2);
        var exhausted = _Delivery(redeliveryCount: 5);
        tracker.OnRejected(exhausted);

        // when
        _time.Advance(TimeSpan.FromSeconds(1));
        await tracker.RedeliverDueAsync(AbortToken);

        // then: the message is never dropped between the two topics
        _receiver.Redelivered.Should().ContainSingle().Which.Should().Be(exhausted.MessageId);
    }

    [Fact]
    public async Task should_report_a_failed_redelivery_request()
    {
        // given
        var receiver = Substitute.For<IPulsarReceiver>();
        receiver.Settles.Returns(true);
        receiver.Topic.Returns(_Topic);
        receiver
            .RedeliverAsync(Arg.Any<IEnumerable<DotPulsar.MessageId>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("broker gone")));
        var tracker = _Tracker();
        tracker.OnRejected(new PulsarDelivery(receiver, PulsarMessages.Create(_Topic)));

        // when
        _time.Advance(TimeSpan.FromSeconds(1));
        await tracker.RedeliverDueAsync(AbortToken);

        // then
        _errors.Should().ContainSingle().Which.Message.Should().Be("broker gone");
    }

    [Theory]
    [InlineData(1_000, null, 100)]
    [InlineData(60_000, null, 1_000)]
    [InlineData(100, null, 10)]
    [InlineData(60_000, 2_000, 200)]
    public void should_tick_a_tenth_of_the_shortest_delay_within_bounds(
        int negativeAckDelayMs,
        int? ackTimeoutMs,
        int expectedTickMs
    )
    {
        // when
        var tracker = new PulsarRedeliveryTracker(
            TimeSpan.FromMilliseconds(negativeAckDelayMs),
            ackTimeoutMs is { } timeout ? TimeSpan.FromMilliseconds(timeout) : null,
            maxRedeliveryCount: null,
            (_, _) => Task.FromResult(true),
            _ => { },
            _time
        );

        // then
        tracker.Tick.Should().Be(TimeSpan.FromMilliseconds(expectedTickMs));
    }

    private PulsarRedeliveryTracker _Tracker(TimeSpan? ackTimeout = null, int? maxRedeliveryCount = null) =>
        new(
            TimeSpan.FromSeconds(1),
            ackTimeout,
            maxRedeliveryCount,
            (delivery, _) =>
            {
                if (_deadLetterSucceeds)
                {
                    _deadLettered.Enqueue(delivery);
                }

                return Task.FromResult(_deadLetterSucceeds);
            },
            _errors.Enqueue,
            _time
        );

    private PulsarDelivery _Delivery(uint redeliveryCount = 0, ulong entryId = 22) =>
        new(_receiver, PulsarMessages.Create(_Topic, entryId: entryId, redeliveryCount: redeliveryCount));
}
