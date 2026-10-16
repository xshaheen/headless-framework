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
        (await _ExchangeExistsAsync(RabbitMqPhysicalAddress.Exchange(exchange, MessageLane.Queue))).Should().BeFalse();
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

    private (ServiceProvider Provider, string Name, string Exchange) _Build()
    {
        var name = $"topology-{Guid.NewGuid():N}";
        var exchange = $"topology-{Guid.NewGuid():N}";
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
            messaging.AddModule<OrderModule>();
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
