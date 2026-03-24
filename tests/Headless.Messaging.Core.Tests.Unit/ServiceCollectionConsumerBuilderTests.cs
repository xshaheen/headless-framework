// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class ServiceCollectionConsumerBuilderTests : TestBase
{
    [Fact]
    public void should_register_consumer_with_topic()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed");
        var provider = services.BuildServiceProvider();

        // then
        var metadata = provider.GetServices<ConsumerMetadata>().ToList();
        metadata.Should().HaveCount(1);
        metadata[0].Topic.Should().Be("orders.placed");
        metadata[0].ConsumerType.Should().Be<TestOrderHandler>();
        metadata[0].MessageType.Should().Be<TestOrderEvent>();
    }

    [Fact]
    public void should_register_consumer_with_group()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed").Group("order-service");
        var provider = services.BuildServiceProvider();

        // then
        var metadata = provider.GetServices<ConsumerMetadata>().Single();
        metadata.Group.Should().Be("order-service");
    }

    [Fact]
    public void should_register_consumer_with_concurrency()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed").Concurrency(10);
        var provider = services.BuildServiceProvider();

        // then
        var metadata = provider.GetServices<ConsumerMetadata>().Single();
        metadata.Concurrency.Should().Be(10);
    }

    [Fact]
    public void should_default_concurrency_to_one()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed");
        var provider = services.BuildServiceProvider();

        // then
        var metadata = provider.GetServices<ConsumerMetadata>().Single();
        metadata.Concurrency.Should().Be(1);
    }

    [Fact]
    public void should_default_group_to_null()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed");
        var provider = services.BuildServiceProvider();

        // then
        var metadata = provider.GetServices<ConsumerMetadata>().Single();
        metadata.Group.Should().BeNull();
    }

    [Fact]
    public void should_support_fluent_configuration()
    {
        // given
        var services = new ServiceCollection();

        // when
        var builder = services
            .AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed")
            .Topic("orders.created")
            .Group("my-group")
            .Concurrency(5);
        var provider = services.BuildServiceProvider();

        // then
        builder.Should().NotBeNull();
        var metadata = provider.GetServices<ConsumerMetadata>().Single();
        metadata.Topic.Should().Be("orders.created");
        metadata.Group.Should().Be("my-group");
        metadata.Concurrency.Should().Be(5);
    }

    [Fact]
    public void should_throw_when_topic_is_null()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddConsumer<TestOrderHandler, TestOrderEvent>(null!);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_topic_is_empty()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddConsumer<TestOrderHandler, TestOrderEvent>("");

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_topic_is_whitespace()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddConsumer<TestOrderHandler, TestOrderEvent>("   ");

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_services_is_null()
    {
        // given
        IServiceCollection services = null!;

        // when
        var act = () => services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed");

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_throw_when_concurrency_is_zero()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed").Concurrency(0);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Concurrency must be greater than 0*");
    }

    [Fact]
    public void should_throw_when_topic_change_is_null()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed");

        // when
        var act = () => builder.Topic(null!);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_topic_change_is_empty()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed");

        // when
        var act = () => builder.Topic("");

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_group_is_null()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed");

        // when
        var act = () => builder.Group(null!);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_group_is_empty()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed");

        // when
        var act = () => builder.Group("");

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_allow_handler_id_override_without_build()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed");
        const string handlerId = "orders.placed.test-order-handler";

        // when
        builder.HandlerId(handlerId);
        var provider = services.BuildServiceProvider();

        // then
        var metadata = provider.GetServices<ConsumerMetadata>().Single();
        metadata.HandlerId.Should().Be(handlerId);
    }

    [Fact]
    public void should_register_consumer_in_di_as_scoped()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed");
        var provider = services.BuildServiceProvider();

        // then
        using var scope = provider.CreateScope();
        var consumer = scope.ServiceProvider.GetService<IConsume<TestOrderEvent>>();
        consumer.Should().NotBeNull();
        consumer.Should().BeOfType<TestOrderHandler>();
    }

    [Fact]
    public void should_not_register_duplicate_consumer_in_di()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed");
        services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.created");
        var provider = services.BuildServiceProvider();

        // then
        using var scope = provider.CreateScope();
        var consumers = scope.ServiceProvider.GetServices<IConsume<TestOrderEvent>>().ToList();
        consumers.Should().HaveCount(1);
    }

    [Fact]
    public void should_update_metadata_when_topic_changes()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed");

        // when
        builder.Topic("orders.updated");
        var provider = services.BuildServiceProvider();

        // then
        var metadata = provider.GetServices<ConsumerMetadata>().Single();
        metadata.Topic.Should().Be("orders.updated");
    }

    [Fact]
    public void should_update_only_the_target_registration_when_same_consumer_is_registered_twice()
    {
        // given
        var services = new ServiceCollection();
        var first = services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed");
        var second = services.AddConsumer<TestOrderHandler, TestOrderEvent>("orders.created");

        // when
        first.Group("first-group");
        second.Topic("orders.created.v2");

        var provider = services.BuildServiceProvider();

        // then
        var metadata = provider.GetServices<ConsumerMetadata>().OrderBy(x => x.Topic, StringComparer.Ordinal).ToList();
        metadata.Should().HaveCount(2);
        metadata.Should().Contain(x => x.Topic == "orders.placed" && x.Group == "first-group");
        metadata.Should().Contain(x => x.Topic == "orders.created.v2" && x.Group == null);
    }

    [Fact]
    public void should_chain_multiple_configuration_changes()
    {
        // given
        var services = new ServiceCollection();

        // when
        services
            .AddConsumer<TestOrderHandler, TestOrderEvent>("initial.topic")
            .Topic("changed.topic")
            .Group("group-1")
            .Concurrency(3)
            .Group("group-2")
            .Concurrency(7);

        var provider = services.BuildServiceProvider();

        // then
        var metadata = provider.GetServices<ConsumerMetadata>().Single();
        metadata.Topic.Should().Be("changed.topic");
        metadata.Group.Should().Be("group-2");
        metadata.Concurrency.Should().Be(7);
    }

    [Fact]
    public void with_circuit_breaker_before_group_registers_with_final_group()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when — circuit breaker BEFORE group
        services
            .AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed")
            .WithCircuitBreaker(cb => cb.FailureThreshold = 3)
            .Group("my-group");

        services.AddHeadlessMessaging(messaging =>
        {
            messaging.UseInMemoryMessageQueue();
            messaging.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();
        var cbRegistry = provider.GetRequiredService<ConsumerCircuitBreakerRegistry>();

        // then — override is registered against the final group name
        cbRegistry.TryGet("my-group", out var opts).Should().BeTrue();
        opts!.FailureThreshold.Should().Be(3);
    }

    [Fact]
    public void with_circuit_breaker_after_group_registers_with_final_group()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when — circuit breaker AFTER group
        services
            .AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed")
            .Group("my-group")
            .WithCircuitBreaker(cb => cb.FailureThreshold = 5);

        services.AddHeadlessMessaging(messaging =>
        {
            messaging.UseInMemoryMessageQueue();
            messaging.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();
        var cbRegistry = provider.GetRequiredService<ConsumerCircuitBreakerRegistry>();

        // then
        cbRegistry.TryGet("my-group", out var opts).Should().BeTrue();
        opts!.FailureThreshold.Should().Be(5);
    }

    [Fact]
    public void with_circuit_breaker_no_longer_requires_group_first()
    {
        // given
        var services = new ServiceCollection();

        // when — WithCircuitBreaker without Group() should not throw
        var act = () =>
            services
                .AddConsumer<TestOrderHandler, TestOrderEvent>("orders.placed")
                .WithCircuitBreaker(cb => cb.FailureThreshold = 3);

        // then
        act.Should().NotThrow();
    }
}

// Test types for consumer registration
public sealed record TestOrderEvent(Guid OrderId, decimal Amount);

public sealed class TestOrderHandler : IConsume<TestOrderEvent>
{
    public ValueTask Consume(ConsumeContext<TestOrderEvent> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
