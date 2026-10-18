// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Messaging.RabbitMq;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// The shared topology as a deploy-time resource: setup creates what the registered consumers and Queue messages need,
/// the host only checks it, clear purges it, and teardown deletes it.
/// </summary>
[Collection<RabbitMqFixture>]
public sealed class RabbitMqTopologyResourceTests(RabbitMqFixture fixture) : TestBase
{
    [Fact]
    public async Task should_set_up_check_clear_and_tear_down_the_shared_topology()
    {
        // given
        var (provider, name, exchange) = _Build();
        await using var _ = provider;
        var queue = RabbitMqPhysicalAddress.Queue(MessageLane.Queue, name, name);
        var topology = (await _ResourcesAsync(provider)).Should().ContainSingle(r => r.Type == "RabbitMQ").Which;
        topology.Name.Should().Be("topology");

        // when
        await provider.SetupResourcesAsync(AbortToken);

        // then
        (await fixture.QueueExistsAsync(queue, AbortToken))
            .Should()
            .BeTrue();
        (await fixture.QueueExistsAsync(queue + ".dlq", AbortToken)).Should().BeTrue();
        await topology.CheckAsync(AbortToken);

        // and when — a message waits in the queue, then the state is cleared
        await _PublishAsync(exchange, name);
        (await _MessageCountAsync(queue)).Should().Be(1, "setup bound the queue to its routing key");
        await provider.ResetResourceStateAsync(AbortToken);

        // then
        (await _MessageCountAsync(queue))
            .Should()
            .Be(0);

        // and when
        await provider.TeardownResourcesAsync(AbortToken);

        // then
        (await fixture.QueueExistsAsync(queue, AbortToken))
            .Should()
            .BeFalse();
        (await fixture.QueueExistsAsync(queue + ".dlq", AbortToken)).Should().BeFalse();
        (await _ExchangeExistsAsync(RabbitMqPhysicalAddress.Exchange(exchange, MessageLane.Queue)))
            .Should()
            .BeTrue("the lane exchanges are shared by every service on the broker, so teardown keeps them");
        await FluentActions
            .Awaiting(() => topology.CheckAsync(AbortToken))
            .Should()
            .ThrowAsync<StatefulResourceException>()
            .WithMessage("*does not exist. Run 'resources setup'*");
    }

    [Fact]
    public async Task should_fail_bootstrap_naming_resources_setup_when_the_topology_is_missing()
    {
        // given
        var (provider, _, _) = _Build();
        await using var _ = provider;

        // when
        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // then — a reachable broker without the topology is a deployment fault, not an outage to retry through
        await act.Should().ThrowAsync<StatefulResourceException>().WithMessage("*Run 'resources setup'*");
    }

    [Fact]
    public async Task should_hold_the_topology_warmup_naming_the_missing_queue_until_resources_setup_runs()
    {
        // given
        var (provider, name, exchange) = _Build();
        await using var _ = provider;
        var queue = RabbitMqPhysicalAddress.Queue(MessageLane.Queue, name, name);
        var warmup = provider.GetServices<IWarmup>().Should().ContainSingle(w => w.Name == "MessagingTopology").Which;

        // The lane exchanges exist, as another service's setup leaves them, so the queue is what is missing.
        var connection = await fixture.GetConnectionAsync();
        await using (var channel = await connection.CreateChannelAsync(cancellationToken: AbortToken))
        {
            foreach (var lane in new[] { MessageLane.Bus, MessageLane.Queue })
            {
                await RabbitMqQueueTopology.EnsureExchangeAsync(
                    channel,
                    RabbitMqPhysicalAddress.Exchange(exchange, lane),
                    RabbitMqPhysicalAddress.ExchangeType(lane),
                    AbortToken
                );
            }
        }

        // when
        var beforeSetup = () => warmup.WarmUpAsync(AbortToken);

        // then
        await beforeSetup
            .Should()
            .ThrowAsync<StatefulResourceException>()
            .WithMessage($"RabbitMQ queue '{queue}' does not exist. Run 'resources setup'*");

        // and when - the deployment creates the topology
        await provider.SetupResourcesAsync(AbortToken);

        // then
        await warmup.WarmUpAsync(AbortToken);
    }

    [Fact]
    public async Task should_consume_once_the_deployment_set_the_topology_up()
    {
        // given
        var (provider, name, exchange) = _Build();
        await using var _ = provider;
        await provider.SetupResourcesAsync(AbortToken);

        // when
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        await _PublishAsync(exchange, name);

        // then
        await provider
            .GetRequiredService<OrderConsumer>()
            .Received.Task.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
    }

    [Fact]
    public async Task should_keep_another_services_queue_and_bindings_when_one_host_tears_down()
    {
        // given — two services share the default exchange name and each consumes its own queue
        var (first, firstName, exchange) = _Build();
        await using var _ = first;
        var (second, secondName, _) = _Build(exchange: exchange);
        await using var __ = second;
        await first.SetupResourcesAsync(AbortToken);
        await second.SetupResourcesAsync(AbortToken);
        var secondQueue = RabbitMqPhysicalAddress.Queue(MessageLane.Queue, secondName, secondName);

        // when
        await first.TeardownResourcesAsync(AbortToken);

        // then — the first service's queues are gone; the shared exchanges, including the Bus lane's that nothing
        // binds here but every host requires, and the second service's binding stay
        (
            await fixture.QueueExistsAsync(
                RabbitMqPhysicalAddress.Queue(MessageLane.Queue, firstName, firstName),
                AbortToken
            )
        )
            .Should()
            .BeFalse();
        (await _ExchangeExistsAsync(RabbitMqPhysicalAddress.Exchange(exchange, MessageLane.Queue))).Should().BeTrue();
        (
            await _ExchangeExistsAsync(
                RabbitMqQueueTopology.DeadLetterExchange(RabbitMqPhysicalAddress.Exchange(exchange, MessageLane.Queue))
            )
        )
            .Should()
            .BeTrue();
        (await _ExchangeExistsAsync(RabbitMqPhysicalAddress.Exchange(exchange, MessageLane.Bus))).Should().BeTrue();
        await (await _ResourcesAsync(second))
            .Single(r => string.Equals(r.Type, "RabbitMQ", StringComparison.Ordinal))
            .CheckAsync(AbortToken);
        await _PublishAsync(exchange, secondName);
        (await _MessageCountAsync(secondQueue)).Should().Be(1, "the second service's binding survived the teardown");

        // and the second service still starts against the remaining topology and consumes what was published
        await second.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        await second.GetRequiredService<OrderConsumer>().Received.Task.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // and when — the last service tears down
        await second.TeardownResourcesAsync(AbortToken);

        // then — its queue goes; the shared exchanges stay
        (await fixture.QueueExistsAsync(secondQueue, AbortToken))
            .Should()
            .BeFalse();
        (await _ExchangeExistsAsync(RabbitMqPhysicalAddress.Exchange(exchange, MessageLane.Queue))).Should().BeTrue();
    }

    [Fact]
    public async Task should_leave_a_queue_this_host_only_publishes_to_untouched_by_clear_and_teardown()
    {
        // given — the consumer service owns the queue; the publisher only sends to it
        var (consumer, name, exchange) = _Build();
        await using var _ = consumer;
        var (publisher, _, _) = _Build(name, exchange, consume: false);
        await using var __ = publisher;
        var queue = RabbitMqPhysicalAddress.Queue(MessageLane.Queue, name, name);
        await consumer.SetupResourcesAsync(AbortToken);
        await publisher.SetupResourcesAsync(AbortToken);
        await _PublishAsync(exchange, name);

        // when
        await publisher.ResetResourceStateAsync(AbortToken);
        await publisher.TeardownResourcesAsync(AbortToken);

        // then
        (await fixture.QueueExistsAsync(queue + ".dlq", AbortToken))
            .Should()
            .BeTrue();
        (await _MessageCountAsync(queue))
            .Should()
            .Be(1, "the publisher neither purged nor deleted the consumer's queue");
        (await _ExchangeExistsAsync(RabbitMqPhysicalAddress.Exchange(exchange, MessageLane.Queue))).Should().BeTrue();
    }

    private (ServiceProvider Provider, string Name, string Exchange) _Build(
        string? name = null,
        string? exchange = null,
        bool consume = true
    )
    {
        name ??= $"topology-{Guid.NewGuid():N}";
        exchange ??= $"topology-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<OrderConsumer>();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseRabbitMq(options =>
            {
                options.HostName = fixture.HostName;
                options.Port = fixture.Port;
                options.UserName = fixture.UserName;
                options.Password = fixture.Password;
                options.ExchangeName = exchange;
                options.QueueArguments.EnableDeadLettering = true;
            });
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<Order>(name, "1").OnQueue(queue => queue.RawPayload());

            if (consume)
            {
                messaging.AddModule<OrderModule>();
            }
        });

        return (services.BuildServiceProvider(), name, exchange);
    }

    private static async Task<List<IStatefulResource>> _ResourcesAsync(IServiceProvider provider)
    {
        var resources = new List<IStatefulResource>();

        foreach (var source in provider.GetServices<IStatefulResourceSource>())
        {
            resources.AddRange(await source.GetResourcesAsync(AbortToken));
        }

        return resources;
    }

    private async Task _PublishAsync(string exchange, string name)
    {
        var connection = await fixture.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: AbortToken);
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            Persistent = true,
            MessageId = $"producer-{Guid.NewGuid():N}",
        };

        await channel.BasicPublishAsync(
            RabbitMqPhysicalAddress.Exchange(exchange, MessageLane.Queue),
            RabbitMqPhysicalAddress.RoutingKey(MessageLane.Queue, name),
            mandatory: true,
            properties,
            """{"value":"first"}"""u8.ToArray(),
            AbortToken
        );
    }

    private async Task<uint> _MessageCountAsync(string queue)
    {
        var connection = await fixture.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: AbortToken);

        return (await channel.QueueDeclarePassiveAsync(queue, AbortToken)).MessageCount;
    }

    private async Task<bool> _ExchangeExistsAsync(string exchange)
    {
        var connection = await fixture.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: AbortToken);

        try
        {
            await channel.ExchangeDeclarePassiveAsync(exchange, AbortToken);

            return true;
        }
        catch (OperationInterruptedException e) when (e.ShutdownReason?.ReplyCode == Constants.NotFound)
        {
            return false;
        }
    }

    public sealed record Order(string Value);

    public sealed class OrderModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddQueueConsumer<OrderConsumer, Order>(
                "tests.rabbitmq.topology",
                TestConsumerDispatch.FromServices<OrderConsumer, Order>()
            );
    }

    public sealed class OrderConsumer : IConsume<Order>
    {
        public TaskCompletionSource Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask ConsumeAsync(ConsumeContext<Order> context, CancellationToken cancellationToken)
        {
            Received.TrySetResult();

            return ValueTask.CompletedTask;
        }
    }
}
