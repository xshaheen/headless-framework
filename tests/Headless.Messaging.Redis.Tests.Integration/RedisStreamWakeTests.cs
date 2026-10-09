// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;

namespace Tests;

[Collection<RedisMessagingFixture>]
public sealed class RedisStreamWakeTests(RedisMessagingFixture fixture) : TestBase
{
    // Far longer than any read the tests wait for, so a delivery inside the receive timeout proves a wake-up read it.
    private static readonly TimeSpan _PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _WakeReadTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task should_read_a_message_published_to_an_idle_queue_stream_before_the_next_poll()
    {
        // given
        var destination = $"wake-{Guid.NewGuid():N}";
        await using var session = await fixture.CreateSessionAsync(
            MessageLane.Queue,
            destination,
            destination,
            AbortToken,
            listeningTimeout: _PollInterval
        );
        await session.StartAsync(cancellationToken: AbortToken);
        await _WaitUntilIdleAsync(destination, MessageLane.Queue);
        var message = _Message(destination, MessageLane.Queue);

        // when
        var elapsed = Stopwatch.StartNew();
        (await session.PublishAsync(message, AbortToken)).Succeeded.Should().BeTrue();
        var delivery = await session.ReceiveAsync(_WakeReadTimeout, AbortToken);

        // then
        elapsed.Elapsed.Should().BeLessThan(_WakeReadTimeout);
        delivery.Message.Id.Should().Be(message.Id);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
    }

    [Fact]
    public async Task should_read_a_message_published_to_an_idle_stream_for_an_every_instance_consumer_before_the_next_poll()
    {
        // given
        var destination = $"wake-{Guid.NewGuid():N}";
        var identity = $"group-{Guid.NewGuid():N}";
        await using var session = await fixture.CreateSessionAsync(
            MessageLane.Bus,
            destination,
            identity,
            AbortToken,
            request: new ConsumerClientRequest(
                identity,
                1,
                MessageLane.Bus,
                ConsumerSubscriptionKind.EveryInstance,
                Guid.NewGuid()
            ),
            listeningTimeout: _PollInterval
        );
        await session.StartAsync(cancellationToken: AbortToken);
        await _WaitUntilIdleAsync(destination, MessageLane.Bus);
        var message = _Message(destination, MessageLane.Bus);

        // when
        (await session.PublishAsync(message, AbortToken))
            .Succeeded.Should()
            .BeTrue();
        var delivery = await session.ReceiveAsync(_WakeReadTimeout, AbortToken);

        // then
        delivery.Message.Id.Should().Be(message.Id);
    }

    [Fact]
    public async Task should_unsubscribe_from_the_wake_channel_when_the_consumer_disposes()
    {
        // given
        var destination = $"wake-{Guid.NewGuid():N}";
        var channel = RedisPhysicalAddress.WakeChannel(RedisPhysicalAddress.QueueStream(destination));
        var session = await fixture.CreateSessionAsync(
            MessageLane.Queue,
            destination,
            destination,
            AbortToken,
            listeningTimeout: _PollInterval
        );

        try
        {
            await session.StartAsync(cancellationToken: AbortToken);
            await _WaitUntilIdleAsync(destination, MessageLane.Queue);
        }
        finally
        {
            // when
            await session.DisposeAsync();
        }

        // then
        using var timeout = TimeSpan.FromSeconds(5).ToCancellationTokenSource(AbortToken);
        while (await fixture.CountSubscribersAsync(channel, timeout.Token) > 0)
        {
            await Task.Delay(50, timeout.Token);
        }
    }

    [Fact]
    public async Task should_not_subscribe_to_wake_ups_when_they_are_off()
    {
        // given
        var destination = $"wake-{Guid.NewGuid():N}";
        var channel = RedisPhysicalAddress.WakeChannel(RedisPhysicalAddress.QueueStream(destination));
        await using var session = await fixture.CreateSessionAsync(
            MessageLane.Queue,
            destination,
            destination,
            AbortToken,
            configure: options => options.WakeConsumersOnPublish = false,
            listeningTimeout: TimeSpan.FromMilliseconds(200)
        );

        // when
        await session.StartAsync(cancellationToken: AbortToken);
        var message = _Message(destination, MessageLane.Queue);
        (await session.PublishAsync(message, AbortToken)).Succeeded.Should().BeTrue();
        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then: the poll still reads the message
        delivery.Message.Id.Should().Be(message.Id);
        (await fixture.CountSubscribersAsync(channel, AbortToken)).Should().Be(0);
    }

    // The consumer subscribes to the wake channel right before its first read of new entries; once it has, the read
    // has run and the loop is waiting out the poll interval.
    private async Task _WaitUntilIdleAsync(string destination, MessageLane lane)
    {
        var channel = RedisPhysicalAddress.WakeChannel(RedisPhysicalAddress.ForLane(lane, destination));
        using var timeout = TimeSpan.FromSeconds(10).ToCancellationTokenSource(AbortToken);

        while (await fixture.CountSubscribersAsync(channel, timeout.Token) == 0)
        {
            await Task.Delay(50, timeout.Token);
        }

        await Task.Delay(500, AbortToken);
    }

    private static TransportMessage _Message(string destination, MessageLane lane) =>
        new(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = Guid.NewGuid().ToString("N"),
                [Headers.MessageName] = destination,
                [Headers.Intent] = lane.ToString(),
            },
            "wake"u8.ToArray()
        );
}
