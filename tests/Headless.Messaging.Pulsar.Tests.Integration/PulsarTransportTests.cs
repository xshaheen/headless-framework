// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

[Collection("Pulsar")]
public sealed class PulsarTransportTests(PulsarFixture fixture) : TestBase
{
    [Fact]
    public async Task should_register_bus_and_queue_transports_through_public_setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.UsePulsar(fixture.ConnectionString));
        await using var serviceProvider = services.BuildServiceProvider();

        var bus = serviceProvider.GetRequiredService<IBusTransport>();
        var queue = serviceProvider.GetRequiredService<IQueueTransport>();

        bus.Should().NotBeSameAs(queue);
        serviceProvider
            .GetRequiredService<IConsumerClientFactory>()
            .GetType()
            .Name.Should()
            .Be("PulsarConsumerClientFactory");
    }

    [Fact]
    public async Task should_fan_out_bus_delivery_to_distinct_subscriptions()
    {
        var destination = $"persistent://public/default/conf-{Guid.NewGuid():N}";
        await using var first = await fixture.CreateBusSessionAsync(
            $"group-{Guid.NewGuid():N}",
            AbortToken,
            destination
        );
        await using var second = await fixture.CreateBusSessionAsync(
            $"group-{Guid.NewGuid():N}",
            AbortToken,
            destination
        );
        await first.StartAsync(cancellationToken: AbortToken);
        await second.StartAsync(cancellationToken: AbortToken);

        var expectedId = Guid.NewGuid().ToString("N");
        var result = await first.PublishAsync(_CreateMessage(destination, expectedId, MessageLane.Bus), AbortToken);
        result.Succeeded.Should().BeTrue();

        var firstDelivery = await first.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        var secondDelivery = await second.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        firstDelivery.Message.Id.Should().Be(expectedId);
        secondDelivery.Message.Id.Should().Be(expectedId);
        firstDelivery.Message.Headers[MessagingHeaders.Intent].Should().Be(nameof(MessageLane.Bus));
        secondDelivery.Message.Headers[MessagingHeaders.Intent].Should().Be(nameof(MessageLane.Bus));
        await first.Consumer.CommitAsync(firstDelivery.SettlementValue, AbortToken);
        await second.Consumer.CommitAsync(secondDelivery.SettlementValue, AbortToken);
    }

    [Fact]
    public async Task should_compete_queue_delivery_on_fixed_headless_subscription()
    {
        var destination = $"persistent://public/default/conf-{Guid.NewGuid():N}";
        await using var first = await fixture.CreateQueueSessionAsync(AbortToken, destination);
        await using var second = await fixture.CreateQueueSessionAsync(AbortToken, destination);
        await first.StartAsync(cancellationToken: AbortToken);
        await second.StartAsync(cancellationToken: AbortToken);

        var result = await first.PublishAsync(
            _CreateMessage(destination, Guid.NewGuid().ToString("N"), MessageLane.Queue),
            AbortToken
        );
        result.Succeeded.Should().BeTrue();

        using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var firstReceive = first.ReceiveAsync(TimeSpan.FromSeconds(10), receiveCts.Token);
        var secondReceive = second.ReceiveAsync(TimeSpan.FromSeconds(10), receiveCts.Token);
        var winner = await Task.WhenAny(firstReceive, secondReceive);
        var delivery = await winner;
        var winnerSession = ReferenceEquals(winner, firstReceive) ? first : second;
        var loser = ReferenceEquals(winner, firstReceive) ? secondReceive : firstReceive;

        await receiveCts.CancelAsync();
        var loserDelivery = await _ObserveCanceledLoserAsync(loser);
        loserDelivery.Should().BeNull("a shared queue subscription must dispatch each broker message once");
        await winnerSession.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_round_trip_a_compressed_payload_with_producer_settings(bool batching)
    {
        var destination = $"persistent://public/default/conf-{Guid.NewGuid():N}";
        await using var session = await PulsarFixture.CreateSessionAsync(
            fixture.ConnectionString,
            MessageLane.Queue,
            AbortToken,
            destination,
            createReplacement: false,
            configureOptions: options =>
            {
                options.Producer.EnableBatching = batching;
                options.Producer.BatchingMaxPublishDelay = TimeSpan.FromMilliseconds(5);
                options.Producer.CompressionType = Pulsar.Client.Common.CompressionType.LZ4;
                options.Producer.SendTimeout = TimeSpan.FromSeconds(10);
            }
        );
        await session.StartAsync(cancellationToken: AbortToken);

        var body = new byte[1024 * 1024];
        Random.Shared.NextBytes(body);
        var messageId = Guid.NewGuid().ToString("N");
        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = messageId,
                [MessagingHeaders.MessageName] = destination,
            },
            body
        );

        var result = await session.PublishAsync(message, AbortToken);

        result.Succeeded.Should().BeTrue(result.Exception?.ToString());
        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(30), AbortToken);
        delivery.Message.Id.Should().Be(messageId);
        delivery.Message.Body.ToArray().Should().Equal(body);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
    }

    private static async Task<TransportConformanceDelivery?> _ObserveCanceledLoserAsync(
        Task<TransportConformanceDelivery> loser
    )
    {
        try
        {
            return await loser;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static TransportMessage _CreateMessage(string destination, string messageId, MessageLane lane)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = messageId,
            [MessagingHeaders.MessageName] = destination,
            [MessagingHeaders.Intent] = _ToMessageLane(lane).ToString(),
            ["x-headless-conformance"] = "pulsar-intent",
        };

        return new TransportMessage(headers, "pulsar-intent-probe"u8.ToArray());
    }

    private static MessageLane _ToMessageLane(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => MessageLane.Bus,
            MessageLane.Queue => MessageLane.Queue,
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null),
        };
}
