// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class MessagingBuilderTests
{
    private static string _CircuitKey(IntentType intentType, string group)
    {
        return $"{intentType:D}:{group}";
    }

    [Fact]
    public void should_use_message_type_name_as_default_topic()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
            setup.ForMessage<TestOrderMessage>(message => message.OnBus<TestOrderConsumer>())
        );

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetDrainedConsumerRegistry();

        // then
        var orderConsumer = registry.GetAll().First(c => c.ConsumerType == typeof(TestOrderConsumer));
        orderConsumer.MessageName.Should().Be(nameof(TestOrderMessage));
    }

    [Fact]
    public void should_register_consumer_in_di_as_scoped()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
            setup.ForMessage<TestOrderMessage>(message =>
                message.MessageName("orders.placed").OnBus<TestOrderConsumer>()
            )
        );

        using var provider = services.BuildServiceProvider();

        // then
        using var scope = provider.CreateScope();
        var consumer = scope.ServiceProvider.GetService<IConsume<TestOrderMessage>>();
        consumer.Should().NotBeNull();
        consumer.Should().BeOfType<TestOrderConsumer>();
    }

    [Fact]
    public async Task should_register_runtime_and_bootstrap_services()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.UseInMemory();
            messaging.UseInMemoryStorage();
        });

        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IRuntimeSubscriber>().Should().NotBeNull();
        provider.GetRequiredService<IBootstrapper>().Should().NotBeNull();
        provider.GetRequiredService<IBus>().Should().NotBeNull();
        provider.GetRequiredService<IQueue>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxBus>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxQueue>().Should().NotBeNull();
    }

    [Fact]
    public void should_resolve_real_commit_coordinator_when_registered_after_messaging()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(_ => { });
        services.AddCommitCoordination();

        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredService<ICurrentCommitCoordinator>()
            .Should()
            .BeSameAs(provider.GetRequiredService<CommitScopeStack>());
    }

    [Fact]
    public void should_prevent_duplicate_topic_mappings()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessMessaging(messaging =>
            {
                messaging.WithMessageNameMapping<TestOrderMessage>("orders.placed");
                messaging.WithMessageNameMapping<TestOrderMessage>("orders.created");
            });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*already mapped to messageName*");
    }

    [Fact]
    public void should_allow_same_topic_mapping_if_identical()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessMessaging(messaging =>
            {
                messaging.WithMessageNameMapping<TestOrderMessage>("orders.placed");
                messaging.WithMessageNameMapping<TestOrderMessage>("orders.placed");
            });

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_use_explicit_default_group_name_when_configured()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.ForMessage<TestOrderMessage>(message =>
                message.MessageName("orders.placed").OnBus<TestOrderConsumer>()
            );
            messaging.Options.DefaultGroupName = "shared-group";
        });

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetDrainedConsumerRegistry();

        // then
        registry.GetAll().Single().Group.Should().Be("shared-group");
    }

    [Fact]
    public void should_apply_group_name_prefix_to_generated_groups()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.ForMessage<TestOrderMessage>(message =>
                message.MessageName("orders.placed").OnBus<TestOrderConsumer>()
            );
            messaging.Options.GroupNamePrefix = "tenant-a";
            messaging.UseConventions(conventions =>
            {
                conventions.UseApplicationId("orders");
                conventions.UseVersion("v1");
            });
        });

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetDrainedConsumerRegistry();

        // then
        var handlerId = MessagingConventions.GetDefaultHandlerId(typeof(TestOrderConsumer), typeof(TestOrderMessage));
        var conventions = new MessagingConventions().UseApplicationId("orders").UseVersion("v1");
        registry.GetAll().Single().Group.Should().Be($"tenant-a.{conventions.GetGroupName(handlerId)}");
    }

    [Fact]
    public void should_replace_messaging_lock_provider_when_use_distributed_lock_called_twice()
    {
        // given — last-wins semantics: second registration must supersede the first
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);
        var firstProvider = Substitute.For<IDistributedLock>();
        var secondProvider = Substitute.For<IDistributedLock>();

        // when
        builder.UseDistributedLock(firstProvider);
        builder.UseDistributedLock(secondProvider);

        // then — only one descriptor present for the messaging-keyed slot
        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IDistributedLock)
                && d.IsKeyedService
                && Equals(d.ServiceKey, MessagingKeys.LockProvider)
            )
            .ToArray();

        descriptors.Should().ContainSingle();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<IDistributedLock>(MessagingKeys.LockProvider);
        resolved.Should().BeSameAs(secondProvider, "the second registration must win under last-wins semantics");
    }

    [Fact]
    public void with_circuit_breaker_uses_final_group_name()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
            setup.ForMessage<TestOrderMessage>(message =>
                message
                    .MessageName("orders.placed")
                    .OnBus<TestOrderConsumer>(consumer =>
                        consumer.WithCircuitBreaker(cb => cb.FailureThreshold = 3).Group("final-group")
                    )
            )
        );

        using var provider = services.BuildServiceProvider();
        provider.GetDrainedConsumerRegistry();
        var cbRegistry = provider.GetRequiredService<ConsumerCircuitBreakerRegistry>();

        // then
        cbRegistry.TryGet(_CircuitKey(IntentType.Bus, "final-group"), out var opts).Should().BeTrue();
        opts!.FailureThreshold.Should().Be(3);
    }
}

public sealed record TestOrderMessage(string OrderId, decimal Amount);

public sealed record TestPaymentMessage(string PaymentId, decimal Amount);

public sealed class TestOrderConsumer : IConsume<TestOrderMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<TestOrderMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class AnotherOrderConsumer : IConsume<TestOrderMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<TestOrderMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class TestPaymentConsumer : IConsume<TestPaymentMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<TestPaymentMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class MultiMessageConsumer : IConsume<TestOrderMessage>, IConsume<TestPaymentMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<TestOrderMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask ConsumeAsync(ConsumeContext<TestPaymentMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
