// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Guarantees;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Two hosts of one service run a sequential queue consumer. Its queue carries RabbitMQ's single active consumer
/// argument, so the broker delivers to one host's consumer at a time and keeps the other on standby.
/// </summary>
[Collection<RabbitMqFixture>]
public sealed class RabbitMqSequentialActiveConsumerTests(RabbitMqFixture fixture) : TestBase
{
    private const string _ConsumerIdentity = "tests.rabbitmq.sequential";

    [Fact]
    [Guarantee("MSG-LANE-08")]
    public async Task should_deliver_a_keyed_burst_to_only_one_host_in_publish_order()
    {
        // given
        var name = $"sequential-{Guid.NewGuid():N}";
        var exchange = $"sequential-{Guid.NewGuid():N}";
        var deliveries = new Deliveries(count: 20);
        await using var first = _Build(name, exchange, "first", deliveries);
        await using var second = _Build(name, exchange, "second", deliveries);
        await first.ProvisionResourcesAsync(AbortToken);
        await first.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        await second.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        var values = Enumerable.Range(0, deliveries.Count).Select(static i => $"a-{i}").ToArray();

        // when
        await using (var scope = first.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IQueue>();
            foreach (var value in values)
            {
                await queue.EnqueueAsync(new Order(value), new QueueOptions { RoutingAffinityKey = "a" }, AbortToken);
            }
        }

        // then
        await deliveries.AllReceived.Task.WaitAsync(TimeSpan.FromSeconds(60), AbortToken);
        var received = deliveries.Received.ToArray();
        received
            .Select(static x => x.Host)
            .Distinct()
            .Should()
            .ContainSingle("the broker feeds one consumer at a time");
        received.Select(static x => x.Value).Should().Equal(values);
    }

    private ServiceProvider _Build(string name, string exchange, string host, Deliveries deliveries)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(deliveries);
        services.AddSingleton(new HostLabel(host));
        services.AddSingleton<OrderConsumer>();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseRabbitMq(options =>
            {
                options.HostName = fixture.HostName;
                options.VirtualHost = fixture.VirtualHost;
                options.Port = fixture.Port;
                options.UserName = fixture.UserName;
                options.Password = fixture.Password;
                options.ExchangeName = exchange;
            });
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
            setup.Tune(_ConsumerIdentity, consumer => consumer.Concurrency(4).Sequential());
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<Order>(name, "1");
            messaging.AddModule<OrderModule>();
        });

        return services.BuildServiceProvider();
    }

    public sealed record Order(string Value);

    public sealed record HostLabel(string Name);

    public sealed class Deliveries(int count)
    {
        public int Count { get; } = count;

        public ConcurrentQueue<(string Host, string Value)> Received { get; } = new();

        public TaskCompletionSource AllReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Record(string host, string value)
        {
            Received.Enqueue((host, value));

            if (Received.Count >= Count)
            {
                AllReceived.TrySetResult();
            }
        }
    }

    public sealed class OrderModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddQueueConsumer<OrderConsumer, Order>(
                _ConsumerIdentity,
                TestConsumerDispatch.FromServices<OrderConsumer, Order>()
            );
    }

    public sealed class OrderConsumer(HostLabel host, Deliveries deliveries) : IConsume<Order>
    {
        public ValueTask ConsumeAsync(ConsumeContext<Order> context, CancellationToken cancellationToken)
        {
            deliveries.Record(host.Name, context.Message.Value);

            return ValueTask.CompletedTask;
        }
    }
}
