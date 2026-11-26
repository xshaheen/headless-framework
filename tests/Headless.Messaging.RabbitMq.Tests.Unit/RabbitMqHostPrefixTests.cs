// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class RabbitMqHostPrefixTests : TestBase
{
    [Fact]
    public void should_keep_names_unchanged_without_a_prefix()
    {
        RabbitMqPhysicalAddress.BaseExchange("", "messaging.default.router").Should().Be("messaging.default.router");
        RabbitMqPhysicalAddress.Queue("", MessageLane.Bus, "workers", "orders").Should().Be("bus.workers");
        RabbitMqPhysicalAddress.Queue("", MessageLane.Queue, "orders", "orders").Should().Be("queue.orders");
    }

    [Fact]
    public void should_put_the_prefix_in_front_of_the_exchange_and_every_queue()
    {
        var exchange = RabbitMqPhysicalAddress.BaseExchange("wt1", "messaging.default.router");

        exchange.Should().Be("wt1.messaging.default.router");
        RabbitMqPhysicalAddress.Exchange(exchange, MessageLane.Queue).Should().Be("wt1.messaging.default.router.queue");
        RabbitMqPhysicalAddress.Queue("wt1", MessageLane.Bus, "workers", "orders").Should().Be("wt1.bus.workers");
        RabbitMqPhysicalAddress.Queue("wt1", MessageLane.Queue, "orders", "orders").Should().Be("wt1.queue.orders");
        RabbitMqPhysicalAddress.DeadLetterQueue("wt1.queue.orders").Should().Be("headless_dlq.wt1.queue.orders");
    }

    [Fact]
    public void should_keep_routing_keys_unprefixed_because_the_exchange_scopes_them()
    {
        RabbitMqPhysicalAddress.RoutingKey(MessageLane.Bus, "orders").Should().Be("bus.orders");
    }

    [Fact]
    public void should_bound_a_prefixed_bus_queue_to_the_rabbitmq_limit()
    {
        var prefix = new string('p', HeadlessPrefixOptions.MaxLength);

        var queue = RabbitMqPhysicalAddress.Queue(prefix, MessageLane.Bus, new string('a', 300), "orders");

        queue.Should().HaveLength(255);
        queue.Should().StartWith(prefix + ".bus.aaa");
    }

    [Fact]
    public void should_fail_when_the_prefix_pushes_a_queue_lane_queue_past_the_limit()
    {
        // The live queue keeps its name at the limit, and only the dead-letter queue takes the hash fallback.
        var maxLengthName = new string('o', 245);
        var maxLengthQueue = RabbitMqPhysicalAddress.Queue("wt1", MessageLane.Queue, maxLengthName, maxLengthName);
        maxLengthQueue.Should().Be("wt1.queue." + maxLengthName).And.HaveLength(255);
        RabbitMqPhysicalAddress.DeadLetterQueue(maxLengthQueue).Should().MatchRegex("^headless_dlq\\.[0-9a-fA-F]{64}$");

        var name = maxLengthName + "o";

        var act = () => RabbitMqPhysicalAddress.Queue("wt1", MessageLane.Queue, name, name);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'wt1'*Headless:Prefix*255*");
        RabbitMqPhysicalAddress.Queue("", MessageLane.Queue, name, name).Should().Be("queue." + name);
    }

    [Fact]
    public void should_fail_when_the_prefix_pushes_the_exchange_past_the_limit()
    {
        var act = () => RabbitMqPhysicalAddress.BaseExchange("wt1", new string('e', 245));

        act.Should().Throw<InvalidOperationException>().WithMessage("*'wt1'*Headless:Prefix*255*");
    }

    [Fact]
    public async Task should_prefix_the_pool_exchange_of_the_default_and_a_named_transport()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHeadlessPrefix("wt1");
        services.ConfigureAll<RabbitMqMessagingOptions>(options =>
        {
            options.UserName = "headless-tests";
            options.Password = "not-a-real-secret";
        });

        // when
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseRabbitMq("rabbit-internal");
            setup.AddTransport("partner").UseRabbitMq("rabbit-partner");
        });
        await using var provider = services.BuildServiceProvider();

        // then - one prefix in front of each instance's exchange, never two
        var options = provider.GetRequiredService<IOptionsMonitor<RabbitMqMessagingOptions>>();
        options.CurrentValue.HostPrefix.Should().Be("wt1");
        options.Get("Headless.Messaging.Transport.partner").HostPrefix.Should().Be("wt1");
        await using var pool = new ConnectionChannelPool(
            NullLogger<ConnectionChannelPool>.Instance,
            provider.GetRequiredService<IOptions<MessagingOptions>>(),
            Options.Create(options.CurrentValue)
        );
        pool.Exchange.Should().Be("wt1." + RabbitMqMessagingOptions.DefaultExchangeName);
        pool.HostPrefix.Should().Be("wt1");
    }
}
