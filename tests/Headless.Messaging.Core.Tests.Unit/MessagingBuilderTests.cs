// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class MessagingBuilderTests
{
    [Fact]
    public void should_register_consumers_via_scan()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMessages(messaging =>
        {
            messaging.ScanConsumers(typeof(MessagingBuilderTests).Assembly);
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();

        // then
        var consumers = registry.GetAll();
        consumers.Should().NotBeEmpty();
        consumers.Should().Contain(c => c.ConsumerType == typeof(TestOrderConsumer));
        consumers.Should().Contain(c => c.ConsumerType == typeof(TestPaymentConsumer));
    }

    [Fact]
    public void should_use_message_type_name_as_default_topic()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMessages(messaging =>
        {
            messaging.ScanConsumers(typeof(MessagingBuilderTests).Assembly);
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();

        // then
        var orderConsumer = registry.GetAll().First(c => c.ConsumerType == typeof(TestOrderConsumer));
        orderConsumer.Topic.Should().Be(nameof(TestOrderMessage));
    }

    [Fact]
    public void should_register_consumer_explicitly_with_fluent_api()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMessages(messaging =>
        {
            messaging
                .Consumer<TestOrderConsumer>()
                .Topic("orders.placed")
                .Group("order-service")
                .WithConcurrency(5)
                .Build();
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();

        // then
        var consumers = registry.GetAll();
        consumers.Should().HaveCount(1);

        var consumer = consumers[0];
        consumer.ConsumerType.Should().Be<TestOrderConsumer>();
        consumer.Topic.Should().Be("orders.placed");
        consumer.Group.Should().Be("order-service");
        consumer.Concurrency.Should().Be(5);
    }

    [Fact]
    public void should_use_topic_mapping_for_scanned_consumers()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMessages(messaging =>
        {
            messaging.WithTopicMapping<TestOrderMessage>("orders.placed");
            messaging.ScanConsumers(typeof(MessagingBuilderTests).Assembly);
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();

        // then
        var orderConsumer = registry.GetAll().First(c => c.ConsumerType == typeof(TestOrderConsumer));
        orderConsumer.Topic.Should().Be("orders.placed");
    }

    [Fact]
    public void should_override_topic_mapping_with_explicit_topic()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMessages(messaging =>
        {
            messaging.WithTopicMapping<TestOrderMessage>("orders.placed");
            messaging.Consumer<TestOrderConsumer>().Topic("custom.orders").Build();
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();

        // then
        var consumer = registry.GetAll()[0];
        consumer.Topic.Should().Be("custom.orders");
    }

    [Fact]
    public void should_register_consumer_in_di_as_scoped()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMessages(messaging =>
        {
            messaging.Consumer<TestOrderConsumer>().Topic("orders.placed").Build();
        });

        var provider = services.BuildServiceProvider();

        // then
        using var scope = provider.CreateScope();
        var consumer = scope.ServiceProvider.GetService<IConsume<TestOrderMessage>>();
        consumer.Should().NotBeNull();
        consumer.Should().BeOfType<TestOrderConsumer>();
    }

    [Fact]
    public void should_support_multiple_consumers_for_same_message_type()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMessages(messaging =>
        {
            messaging.Consumer<TestOrderConsumer>().Topic("orders.placed").Group("order-service").Build();

            messaging.Consumer<AnotherOrderConsumer>().Topic("orders.placed").Group("analytics-service").Build();
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();

        // then
        var consumers = registry.GetAll();
        consumers.Should().HaveCount(2);
        consumers.Should().Contain(c => c.ConsumerType == typeof(TestOrderConsumer) && c.Group == "order-service");
        consumers
            .Should()
            .Contain(c => c.ConsumerType == typeof(AnotherOrderConsumer) && c.Group == "analytics-service");
    }

    [Fact]
    public void should_throw_when_consumer_does_not_implement_consume()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddMessages(messaging =>
            {
                messaging.Consumer<InvalidConsumer>().Topic("test").Build();
            });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*does not implement IConsume<T>*");
    }

    [Fact]
    public void should_prevent_duplicate_topic_mappings()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddMessages(messaging =>
            {
                messaging.WithTopicMapping<TestOrderMessage>("orders.placed");
                messaging.WithTopicMapping<TestOrderMessage>("orders.created");
            });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*already mapped to topic*");
    }

    [Fact]
    public void should_allow_same_topic_mapping_if_identical()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddMessages(messaging =>
            {
                messaging.WithTopicMapping<TestOrderMessage>("orders.placed");
                messaging.WithTopicMapping<TestOrderMessage>("orders.placed");
            });

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_default_concurrency_to_one()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMessages(messaging =>
        {
            messaging.Consumer<TestOrderConsumer>().Topic("orders.placed").Build();
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();

        // then
        var consumer = registry.GetAll()[0];
        consumer.Concurrency.Should().Be(1);
    }

    [Fact]
    public void should_throw_when_concurrency_is_zero()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
        {
            return services.AddMessages(messaging =>
            {
                messaging.Consumer<TestOrderConsumer>().Topic("orders.placed").WithConcurrency(0).Build();
            });
        };

        // then
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Concurrency must be greater than 0*");
    }

    [Fact]
    public void should_support_multi_message_handlers()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMessages(messaging =>
        {
            messaging.ScanConsumers(typeof(MessagingBuilderTests).Assembly);
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();

        // then
        var multiHandlers = registry.GetAll().Where(c => c.ConsumerType == typeof(MultiMessageConsumer)).ToList();

        multiHandlers.Should().HaveCount(2);
        multiHandlers.Should().Contain(c => c.MessageType == typeof(TestOrderMessage));
        multiHandlers.Should().Contain(c => c.MessageType == typeof(TestPaymentMessage));
    }

    [Fact]
    public void should_chain_fluent_methods()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddMessages(messaging =>
            {
                messaging
                    .WithTopicMapping<TestOrderMessage>("orders.placed")
                    .Consumer<TestOrderConsumer>()
                    .Topic("orders.test")
                    .Group("test-group")
                    .WithConcurrency(3)
                    .Build()
                    .Consumer<TestPaymentConsumer>()
                    .Topic("payments.received")
                    .Build();
            });

        // then
        act.Should().NotThrow();

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();
        var consumers = registry.GetAll();
        consumers.Should().HaveCount(2);
    }

    [Fact]
    public void should_implicitly_create_topic_mapping_when_using_consumer_with_topic()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMessages(messaging =>
        {
            messaging.Consumer<TestOrderConsumer>("orders.placed");
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();

        // then
        var consumer = registry.GetAll()[0];
        consumer.Topic.Should().Be("orders.placed");
        consumer.ConsumerType.Should().Be<TestOrderConsumer>();
    }

    [Fact]
    public void should_eliminate_need_for_separate_topic_mapping_call()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMessages(messaging =>
        {
            // Old way (still supported):
            // messaging.Consumer<TestOrderConsumer>().Topic("orders.placed").Build();
            // messaging.WithTopicMapping<TestOrderMessage>("orders.placed");

            // New way - single call, implicit mapping:
            messaging.Consumer<TestOrderConsumer>("orders.placed");
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();

        // then
        var consumer = registry.GetAll()[0];
        consumer.Topic.Should().Be("orders.placed");
    }

    [Fact]
    public void should_allow_chaining_with_implicit_topic_mapping()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMessages(messaging =>
        {
            messaging.Consumer<TestOrderConsumer>("orders.placed").WithConcurrency(5).Group("order-service").Build();
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();

        // then
        var consumer = registry.GetAll()[0];
        consumer.Topic.Should().Be("orders.placed");
        consumer.Concurrency.Should().Be(5);
        consumer.Group.Should().Be("order-service");
    }
}

// Test message types
public sealed record TestOrderMessage(string OrderId, decimal Amount);

public sealed record TestPaymentMessage(string PaymentId, decimal Amount);

// Test consumer implementations
public sealed class TestOrderConsumer : IConsume<TestOrderMessage>
{
    public ValueTask Consume(ConsumeContext<TestOrderMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class AnotherOrderConsumer : IConsume<TestOrderMessage>
{
    public ValueTask Consume(ConsumeContext<TestOrderMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class TestPaymentConsumer : IConsume<TestPaymentMessage>
{
    public ValueTask Consume(ConsumeContext<TestPaymentMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class MultiMessageConsumer : IConsume<TestOrderMessage>, IConsume<TestPaymentMessage>
{
    public ValueTask Consume(ConsumeContext<TestOrderMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask Consume(ConsumeContext<TestPaymentMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class InvalidConsumer
{
    // Does not implement IConsume<T>
}
