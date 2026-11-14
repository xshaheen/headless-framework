// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Broker delivery evidence for routing affinity keys: a native adapter where the route has one, and the key carried
/// as an envelope header where it has none.
/// </summary>
[PublicAPI]
public static class TransportRoutingAffinityConformance
{
    /// <summary>Exercises the production publisher, real process-local outbox, and production outbox sender against a native observer.</summary>
    public static async Task AssertPublisherPathsAsync(
        Action<MessagingSetupBuilder> configureTransport,
        string destination,
        Func<string, CancellationToken, Task> observeDelivery,
        CancellationToken cancellationToken,
        MessageLane lane = MessageLane.Queue
    )
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            configureTransport(setup);
            setup.UseInMemoryStorage();
        });
        services.ConfigureMessaging(messaging =>
        {
            var contract = messaging.Message<RoutingAffinityProbe>(destination);
            if (lane == MessageLane.Queue)
            {
                contract.OnQueue(queue => queue.RequireRoutingAffinity());
            }
            else
            {
                contract.OnBus(bus => bus.RequireRoutingAffinity());
            }
        });
        await using var provider = services.BuildServiceProvider();
        foreach (var mode in new[] { DeliveryMode.Direct, DeliveryMode.Durable })
        {
            var messageId = Guid.NewGuid().ToString("N");
            if (lane == MessageLane.Queue)
            {
                await provider
                    .GetRequiredService<IQueue>()
                    .EnqueueAsync(
                        new RoutingAffinityProbe("payload"),
                        new QueueOptions
                        {
                            MessageId = messageId,
                            RoutingAffinityKey = "order-42",
                            DeliveryMode = mode,
                        },
                        cancellationToken
                    );
            }
            else
            {
                await provider
                    .GetRequiredService<IBus>()
                    .PublishAsync(
                        new RoutingAffinityProbe("payload"),
                        new PublishOptions
                        {
                            MessageId = messageId,
                            RoutingAffinityKey = "order-42",
                            DeliveryMode = mode,
                        },
                        cancellationToken
                    );
            }

            if (mode == DeliveryMode.Durable)
            {
                var storage = provider.GetRequiredService<IDataStorage>();
                var monitoring = storage.GetMonitoringApi();
                var query = new MessageQuery
                {
                    MessageType = MessageType.Publish,
                    Name = destination,
                    Lane = lane,
                    PageSize = 10,
                };
                var page = await monitoring.GetMessagesAsync(query, cancellationToken);
                var row = page.Items.Should().ContainSingle().Subject;
                var stored = await monitoring.GetPublishedMessageAsync(row.StorageId, cancellationToken);
                stored!.RoutingAffinityKey.Should().Be("order-42");

                // Monitoring exposes the live in-memory row; production pickup returns a detached, leased snapshot.
                clock.Advance(stored.NextRetryAt!.Value - clock.GetUtcNow() + TimeSpan.FromTicks(1));
                var picked = (await storage.GetPublishedMessagesOfNeedRetryAsync(lane, cancellationToken))
                    .Should()
                    .ContainSingle()
                    .Subject;
                picked.StorageId.Should().Be(row.StorageId);
                picked.Origin = provider.GetRequiredService<IMessageSerializer>().Deserialize(picked.Content)!;
                picked.RoutingAffinityKey.Should().Be("order-42");
                var result = await provider.GetRequiredService<IMessageSender>().SendAsync(picked);
                result.Succeeded.Should().BeTrue();
                var completed = await monitoring.GetMessagesAsync(query, cancellationToken);
                completed.Items.Should().ContainSingle().Which.StatusName.Should().Be(StatusName.Succeeded);
            }

            try
            {
                await observeDelivery(messageId, cancellationToken);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Native {lane} observer failed after {mode} publication to '{destination}'.",
                    exception
                );
            }
        }
    }

    private sealed record RoutingAffinityProbe(string Value);

    public static async Task AssertAsync(TransportProviderConformanceDriver driver, CancellationToken cancellationToken)
    {
        await driver.AssertNativePublisherPathsAsync(cancellationToken);
        var identity = Guid.NewGuid().ToString("N");
        var endpoint = new TransportConformanceEndpoint(
            MessageLane.Queue,
            $"affinity-{identity}",
            $"affinity-{identity}",
            "replica-1"
        );
        await using var session = await driver.CreateRoutingAffinitySessionAsync(endpoint, cancellationToken);
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = identity,
            [Headers.MessageName] = session.Destination,
            [Headers.Intent] = nameof(MessageLane.Queue),
            [Headers.RoutingAffinityKey] = "order-42",
        };
        var message = new TransportMessage(headers, "affinity"u8.ToArray());
        if (!driver.SupportsRoutingAffinity)
        {
            // No native mapping: the publish succeeds and the key reaches the consumer as the envelope header, on the
            // first delivery and on a redelivery, which is what a sequential consumer orders by.
            await session.StartAsync(cancellationToken: cancellationToken);
            (await session.PublishAsync(message, cancellationToken)).Succeeded.Should().BeTrue();
            var carried = await session.ReceiveAsync(TimeSpan.FromSeconds(30), cancellationToken);
            carried.Message.Id.Should().Be(identity);
            carried.Message.RoutingAffinityKey.Should().Be("order-42");
            await session.Consumer.RejectAsync(carried.SettlementValue, cancellationToken);
            var redelivered = await session.ReceiveAsync(TimeSpan.FromSeconds(30), cancellationToken);
            redelivered.Message.Id.Should().Be(identity);
            redelivered.Message.RoutingAffinityKey.Should().Be("order-42");
            await session.Consumer.CommitAsync(redelivered.SettlementValue, cancellationToken);
            return;
        }

        await session.StartAsync(cancellationToken: cancellationToken);
        (await session.PublishAsync(message, cancellationToken)).Succeeded.Should().BeTrue();
        var first = await session.ReceiveAsync(TimeSpan.FromSeconds(30), cancellationToken);
        first.Message.RoutingAffinityKey.Should().Be("order-42");
        driver.AssertNativeRoutingAffinity(first, "order-42");
        var placement = driver.GetNativeRoutingPlacement(first);
        await session.Consumer.RejectAsync(first.SettlementValue, cancellationToken);
        var retry = await session.ReceiveAsync(TimeSpan.FromSeconds(30), cancellationToken);
        retry.Message.Id.Should().Be(identity);
        retry.Message.RoutingAffinityKey.Should().Be("order-42");
        driver.AssertNativeRoutingAffinity(retry, "order-42");
        driver.GetNativeRoutingPlacement(retry).Should().Be(placement);
        await session.Consumer.CommitAsync(retry.SettlementValue, cancellationToken);

        await AssertPublisherPathsAsync(
            driver.ConfigureRoutingAffinityTransport,
            session.Destination,
            async (expectedId, token) =>
            {
                var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(30), token);
                delivery.Message.Id.Should().Be(expectedId);
                delivery.Message.RoutingAffinityKey.Should().Be("order-42");
                driver.AssertNativeRoutingAffinity(delivery, "order-42");
                driver.GetNativeRoutingPlacement(delivery).Should().Be(placement);
                await session.Consumer.CommitAsync(delivery.SettlementValue, token);
            },
            cancellationToken
        );
    }
}
