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

        bus.Should().BeSameAs(queue);
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
        firstDelivery.Message.Headers[MessagingHeaders.Intent].Should().Be(nameof(IntentType.Bus));
        secondDelivery.Message.Headers[MessagingHeaders.Intent].Should().Be(nameof(IntentType.Bus));
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
            [MessagingHeaders.Intent] = _ToIntentType(lane).ToString(),
            ["x-headless-conformance"] = "pulsar-intent",
        };

        return new TransportMessage(headers, "pulsar-intent-probe"u8.ToArray());
    }

    private static IntentType _ToIntentType(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => IntentType.Bus,
            MessageLane.Queue => IntentType.Queue,
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null),
        };
}
