// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using NATS.Client.JetStream;

namespace Tests;

public sealed class NatsJSDeliveryTests : TestBase
{
    [Fact]
    public async Task should_return_from_the_constructor_when_the_progress_interval_and_ack_complete_at_once()
    {
        // given - a one-tick AckWait halves to a zero interval, and the substitute's progress ack completes synchronously
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();

        // when
        var create = Task.Run(() => new NatsJSDelivery(msg, TimeSpan.FromTicks(1), TimeProvider.System, _ => { }));
        await using var delivery = await create.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        delivery.Msg.Should().BeSameAs(msg);
    }

    [Fact]
    public async Task should_return_from_the_constructor_when_the_progress_interval_exceeds_the_longest_delay()
    {
        // given - half of this AckWait is longer than Task.Delay accepts, so the delay throws before it awaits
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();

        // when
        var create = Task.Run(() => new NatsJSDelivery(msg, TimeSpan.FromDays(200), TimeProvider.System, _ => { }));
        await using var delivery = await create.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        delivery.Msg.Should().BeSameAs(msg);
    }

    [Fact]
    public async Task should_report_progress_once_when_half_the_ack_wait_elapses_right_after_construction()
    {
        // given
        var clock = new FakeTimeProvider();
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        await using var delivery = new NatsJSDelivery(msg, TimeSpan.FromSeconds(30), clock, _ => { });

        // when
        clock.Advance(TimeSpan.FromSeconds(15));

        // then
        await msg.Received(1).AckProgressAsync(Arg.Any<AckOpts?>(), Arg.Any<CancellationToken>());
    }
}
