// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Internal;

public sealed class DeliverWithinPublishTests : TestBase
{
    private static readonly DateTimeOffset _Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void should_stamp_absolute_utc_deadline_from_publish_time_when_deliver_within_is_set()
    {
        // given
        var factory = _CreateFactory(new FakeTimeProvider(_Now));

        // when
        var prepared = factory.Create(
            new OrderPlaced("o-1"),
            new PublishOptions { MessageName = "orders.placed", DeliverWithin = TimeSpan.FromMinutes(5) }
        );

        // then
        var stamped = prepared.Message.Headers[Headers.DeliverBy];
        stamped.Should().Be(_Now.AddMinutes(5).ToString("O", CultureInfo.InvariantCulture));
        DeliverByEnvelope.GetDeadline(prepared.Message.Headers).Should().Be(_Now.AddMinutes(5));
    }

    [Fact]
    public void should_not_stamp_deadline_when_deliver_within_is_not_set()
    {
        // given
        var factory = _CreateFactory(new FakeTimeProvider(_Now));

        // when
        var prepared = factory.Create(new OrderPlaced("o-1"), new PublishOptions { MessageName = "orders.placed" });

        // then
        prepared.Message.Headers.Should().NotContainKey(Headers.DeliverBy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_non_positive_deliver_within_when_publishing(int seconds)
    {
        // given
        var factory = _CreateFactory(new FakeTimeProvider(_Now));
        var options = new PublishOptions
        {
            MessageName = "orders.placed",
            DeliverWithin = TimeSpan.FromSeconds(seconds),
        };

        // when
        var act = () => factory.Create(new OrderPlaced("o-1"), options);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*DeliverWithin*positive*");
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(10, 5)]
    public void should_reject_delay_that_reaches_the_deadline_when_publishing(
        int delayMinutes,
        int deliverWithinMinutes
    )
    {
        // given — a message due at or after its deadline could never be delivered
        var factory = _CreateFactory(new FakeTimeProvider(_Now));
        var options = new QueueOptions
        {
            MessageName = "orders.placed",
            DeliverWithin = TimeSpan.FromMinutes(deliverWithinMinutes),
        };

        // when
        var act = () =>
            factory.Create(
                new OrderPlaced("o-1"),
                options,
                delayTime: TimeSpan.FromMinutes(delayMinutes),
                lane: MessageLane.Queue
            );

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*DeliverWithin*could never be delivered*");
    }

    [Fact]
    public void should_accept_delay_shorter_than_deliver_within_and_measure_deadline_from_publish_time()
    {
        // given
        var factory = _CreateFactory(new FakeTimeProvider(_Now));
        var options = new OutboxOptions { MessageName = "orders.placed", DeliverWithin = TimeSpan.FromMinutes(10) };

        // when
        var prepared = factory.Create(new OrderPlaced("o-1"), options, delayTime: TimeSpan.FromMinutes(3));

        // then — the deadline counts from the publish call, not from when the delay ends
        DeliverByEnvelope.GetDeadline(prepared.Message.Headers).Should().Be(_Now.AddMinutes(10));
    }

    [Fact]
    public void should_reject_absolute_schedule_at_or_after_the_deadline_when_publishing()
    {
        // given
        var factory = _CreateFactory(new FakeTimeProvider(_Now));
        var options = new PublishOptions { MessageName = "orders.placed", DeliverWithin = TimeSpan.FromMinutes(1) };

        // when
        var act = () =>
            factory.Create(
                new OrderPlaced("o-1"),
                typeof(OrderPlaced),
                options,
                delayTime: null,
                publishAt: _Now.AddMinutes(2),
                MessageLane.Bus
            );

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*scheduled at*");
    }

    [Fact]
    public void should_reject_custom_deliver_by_header_when_publishing()
    {
        // given — the literal pins the wire name so a renamed constant cannot free it for custom headers
        var factory = _CreateFactory(new FakeTimeProvider(_Now));
        var options = new PublishOptions
        {
            MessageName = "orders.placed",
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["headless-deliver-by"] = _Now.AddYears(1).ToString("O", CultureInfo.InvariantCulture),
            },
        };

        // when
        var act = () => factory.Create(new OrderPlaced("o-1"), options);

        // then
        Headers.DeliverBy.Should().Be("headless-deliver-by");
        act.Should().Throw<InvalidOperationException>().WithMessage("*headless-deliver-by*reserved*");
    }

    [Fact]
    public void should_carry_deliver_within_when_built_by_publish_and_queue_builders()
    {
        // when
        var publish = new PublishOptionsBuilder().WithDeliverWithin(TimeSpan.FromSeconds(30)).Build();
        var queue = new QueueOptionsBuilder().WithDeliverWithin(TimeSpan.FromSeconds(45)).Build();
        var cleared = new QueueOptionsBuilder()
            .WithDeliverWithin(TimeSpan.FromSeconds(45))
            .WithDeliverWithin(null)
            .Build();

        // then
        publish.DeliverWithin.Should().Be(TimeSpan.FromSeconds(30));
        queue.DeliverWithin.Should().Be(TimeSpan.FromSeconds(45));
        cleared.DeliverWithin.Should().BeNull();
    }

    [Fact]
    public void should_compare_deliver_within_in_option_equality_and_hash_code()
    {
        // given
        var a = new PublishOptions { DeliverWithin = TimeSpan.FromSeconds(30) };
        var same = new PublishOptions { DeliverWithin = TimeSpan.FromSeconds(30) };
        var different = new PublishOptions { DeliverWithin = TimeSpan.FromSeconds(31) };

        // then
        a.Should().Be(same);
        a.GetHashCode().Should().Be(same.GetHashCode());
        a.Should().NotBe(different);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("not-a-date", false)]
    [InlineData("2026-10-15T11:59:59.0000000+00:00", true)]
    [InlineData("2026-10-15T12:00:00.0000000+00:00", true)]
    [InlineData("2026-10-15T12:00:01.0000000+00:00", false)]
    public void should_treat_only_a_readable_past_deadline_as_expired(string? value, bool expired)
    {
        // given — an unreadable value is ignored, so a malformed foreign header cannot discard messages
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (value is not null)
        {
            headers[Headers.DeliverBy] = value;
        }

        // then
        DeliverByEnvelope.IsExpired(headers, _Now).Should().Be(expired);
    }

    private static MessagePublishRequestFactory _CreateFactory(TimeProvider timeProvider)
    {
        return new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            timeProvider,
            Options.Create(new MessagingOptions()),
            new ConsumerRegistry(),
            new NullCurrentTenant()
        );
    }

    private sealed record OrderPlaced(string OrderId);
}
