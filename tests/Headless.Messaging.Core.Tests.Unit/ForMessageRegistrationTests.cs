// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class ForMessageRegistrationTests
{
    [Fact]
    public void should_stash_message_registration_with_bus_consumer()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(setup =>
            setup.ForMessage<OrderPlaced>(message => message.OnBus<OrderPlacedHandler>())
        );
        using var provider = services.BuildServiceProvider();

        // then
        var registration = provider.GetServices<MessageRegistration>().Single();
        registration.MessageType.Should().Be<OrderPlaced>();
        registration.MessageName.Should().BeNull();
        registration.Consumers.Should().ContainSingle();
        registration.Consumers[0].ConsumerType.Should().Be<OrderPlacedHandler>();
        registration.Consumers[0].IntentType.Should().Be(IntentType.Bus);
    }

    [Fact]
    public void should_register_multiple_consumers_for_one_message()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(setup =>
        {
            setup.ForMessage<OrderPlaced>(message =>
            {
                message.OnBus<OrderPlacedHandler>();
                message.OnBus<OrderPlacedAnalyticsHandler>();
            });
        });

        using var provider = services.BuildServiceProvider();

        // then
        var consumers = provider.GetServices<MessageRegistration>().Single().Consumers;
        consumers.Should().HaveCount(2);
        consumers.Should().Contain(consumer => consumer.ConsumerType == typeof(OrderPlacedHandler));
        consumers.Should().Contain(consumer => consumer.ConsumerType == typeof(OrderPlacedAnalyticsHandler));
    }

    [Fact]
    public void should_register_same_consumer_under_bus_and_queue_lanes()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(setup =>
        {
            setup.ForMessage<OrderPlaced>(message =>
            {
                message.OnBus<OrderPlacedHandler>();
                message.OnQueue<OrderPlacedHandler>();
            });
        });

        using var provider = services.BuildServiceProvider();

        // then
        var consumers = provider.GetServices<MessageRegistration>().Single().Consumers;
        consumers.Should().HaveCount(2);
        consumers.Should().Contain(consumer => consumer.IntentType == IntentType.Bus);
        consumers.Should().Contain(consumer => consumer.IntentType == IntentType.Queue);
    }

    [Fact]
    public void should_support_publisher_only_message_name_registration()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.ForMessage<OrderPlaced>(message => message.MessageName("orders.placed"));
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        var registry = provider.GetRequiredService<ConsumerRegistry>();
        registry.GetAll().Should().BeEmpty();
        provider
            .GetRequiredService<IOptions<MessagingOptions>>()
            .Value.MessageNameMappings[typeof(OrderPlaced)]
            .Should()
            .Be("orders.placed");
    }

    [Fact]
    public void should_apply_consumer_configuration_to_discovered_metadata()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.ForMessage<OrderPlaced>(message =>
                message
                    .MessageName("orders.placed")
                    .OnQueue<OrderPlacedHandler>(consumer =>
                        consumer.Group("orders").Concurrency(3).HandlerId("handler-1")
                    )
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        var metadata = provider.GetRequiredService<ConsumerRegistry>().GetAll().Single();
        metadata.MessageName.Should().Be("orders.placed");
        metadata.Group.Should().Be("orders");
        metadata.Concurrency.Should().Be(3);
        metadata.HandlerId.Should().Be("handler-1");
        metadata.IntentType.Should().Be(IntentType.Queue);
    }

    [Fact]
    public void should_throw_when_concurrency_is_zero()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessMessaging(setup =>
                setup.ForMessage<OrderPlaced>(message =>
                    message.OnBus<OrderPlacedHandler>(consumer => consumer.Concurrency(0))
                )
            );

        // then
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Concurrency must be greater than 0*");
    }

    [Fact]
    public void should_merge_same_message_type_registrations()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.ForMessage<OrderPlaced>(message => message.MessageName("orders.placed").OnBus<OrderPlacedHandler>());
            setup.ForMessage<OrderPlaced>(message => message.OnQueue<OrderPlacedAnalyticsHandler>());
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        var consumers = provider.GetRequiredService<ConsumerRegistry>().GetAll();
        consumers.Should().HaveCount(2);
        consumers.Should().OnlyContain(consumer => consumer.MessageName == "orders.placed");
    }

    [Fact]
    public void should_skip_duplicate_same_consumer_registration()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.ForMessage<OrderPlaced>(message => message.MessageName("orders.placed").OnBus<OrderPlacedHandler>());
            setup.ForMessage<OrderPlaced>(message => message.OnBus<OrderPlacedHandler>());
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<ConsumerRegistry>().GetAll().Should().ContainSingle();
    }

    [Fact]
    public void should_reject_duplicate_same_consumer_registration_with_conflicting_circuit_breaker()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessMessaging(static setup =>
            {
                setup.ForMessage<OrderPlaced>(message =>
                    message.OnBus<OrderPlacedHandler>(consumer =>
                        consumer.WithCircuitBreaker(options => options.FailureThreshold = 3)
                    )
                );
                setup.ForMessage<OrderPlaced>(message =>
                    message.OnBus<OrderPlacedHandler>(consumer =>
                        consumer.WithCircuitBreaker(options => options.FailureThreshold = 5)
                    )
                );
                setup.UseInMemory();
                setup.UseInMemoryStorage();
            });

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*registered more than once*conflicting settings*");
    }

    [Fact]
    public void should_reject_different_consumers_with_same_name_group_and_intent()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
        {
            services.AddHeadlessMessaging(static setup =>
            {
                setup.ForMessage<OrderPlaced>(message =>
                    message.MessageName("orders.placed").OnBus<OrderPlacedHandler>(consumer => consumer.Group("orders"))
                );
                setup.ForMessage<OrderPlaced>(message =>
                    message.OnBus<OrderPlacedAnalyticsHandler>(consumer => consumer.Group("orders"))
                );
                setup.UseInMemory();
                setup.UseInMemoryStorage();
            });
        };

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Duplicate consumer registration detected for messageName/group identity*");
    }

    [Fact]
    public void should_reject_conflicting_explicit_names_for_same_message_type()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessMessaging(static setup =>
            {
                setup.ForMessage<OrderPlaced>(message => message.MessageName("orders.placed"));
                setup.ForMessage<OrderPlaced>(message => message.MessageName("orders.placed.v2"));
                setup.UseInMemory();
                setup.UseInMemoryStorage();
            });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*already mapped*");
    }

    [Fact]
    public async Task should_reject_cross_type_message_name_collisions_at_startup()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(static setup =>
        {
            setup.ForMessage<OrderPlaced>(message => message.MessageName("orders.same").OnBus<OrderPlacedHandler>());
            setup.ForMessage<OtherOrderPlaced>(message =>
                message.MessageName("orders.same").OnBus<OtherOrderPlacedHandler>()
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var act = () =>
            provider.GetRequiredService<IBootstrapper>().BootstrapAsync(TestContext.Current.CancellationToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*orders.same*OrderPlaced*OtherOrderPlaced*");
    }

    [Fact]
    public async Task should_reject_cross_type_message_name_collisions_ignoring_case_at_startup()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(static setup =>
        {
            setup.ForMessage<OrderPlaced>(message => message.MessageName("orders.same").OnBus<OrderPlacedHandler>());
            setup.ForMessage<OtherOrderPlaced>(message =>
                message.MessageName("Orders.Same").OnBus<OtherOrderPlacedHandler>()
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var act = () =>
            provider.GetRequiredService<IBootstrapper>().BootstrapAsync(TestContext.Current.CancellationToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*orders.same*OrderPlaced*OtherOrderPlaced*");
    }

    [Fact]
    public void should_scan_assembly_consumers_as_bus_registrations()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.ForMessagesFromAssemblyContaining<ForMessageRegistrationTests>();
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        var consumers = provider.GetRequiredService<ConsumerRegistry>().GetAll();
        consumers.Should().Contain(consumer => consumer.ConsumerType == typeof(OrderPlacedHandler));
        consumers.Should().OnlyContain(consumer => consumer.IntentType == IntentType.Bus);
    }

    [Fact]
    public void should_not_scan_explicitly_registered_consumer_into_bus_lane()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.ForMessage<OrderPlaced>(message => message.OnQueue<OrderPlacedHandler>());
            setup.ForMessagesFromAssemblyContaining<ForMessageRegistrationTests>();
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        var registrations = provider
            .GetRequiredService<ConsumerRegistry>()
            .GetAll()
            .Where(consumer => consumer.ConsumerType == typeof(OrderPlacedHandler))
            .ToList();

        registrations.Should().ContainSingle();
        registrations[0].IntentType.Should().Be(IntentType.Queue);
    }

    private sealed record OrderPlaced;

    private sealed record OtherOrderPlaced;

    private sealed class OrderPlacedHandler : IConsume<OrderPlaced>
    {
        public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderPlacedAnalyticsHandler : IConsume<OrderPlaced>
    {
        public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OtherOrderPlacedHandler : IConsume<OtherOrderPlaced>
    {
        public ValueTask ConsumeAsync(ConsumeContext<OtherOrderPlaced> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }
}
