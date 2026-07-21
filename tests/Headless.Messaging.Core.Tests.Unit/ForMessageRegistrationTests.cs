// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Internal;
using Headless.Messaging.Registration;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
namespace Tests;

public sealed class ForMessageRegistrationTests : TestBase
{
    [Fact]
    public void should_stash_message_registration_with_bus_consumer()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(setup =>
            setup.Bus.ForMessage<OrderPlaced>(message => message.Consumer<OrderPlacedHandler>())
        );
        using var provider = services.BuildServiceProvider();

        // then
        var registration = provider.GetServices<MessageRegistration>().Single();
        registration.MessageType.Should().Be<OrderPlaced>();
        registration.Lane.Should().Be(MessageLane.Bus);
        registration.MessageName.Should().BeNull();
        registration.Consumers.Should().ContainSingle();
        registration.Consumers[0].ConsumerType.Should().Be<OrderPlacedHandler>();
    }

    [Fact]
    public void should_register_multiple_consumers_for_one_message()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(setup =>
        {
            setup.Bus.ForMessage<OrderPlaced>(message =>
            {
                message.Consumer<OrderPlacedHandler>();
                message.Consumer<OrderPlacedAnalyticsHandler>();
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
            setup.Bus.ForMessage<OrderPlaced>(message =>
                message.MessageName("orders.placed").Consumer<OrderPlacedHandler>()
            );
            setup.Queue.ForMessage<OrderPlaced>(message =>
                message.MessageName("orders.placed").Consumer<OrderPlacedHandler>()
            );
        });

        using var provider = services.BuildServiceProvider();

        // then
        var registrations = provider.GetServices<MessageRegistration>().ToArray();
        registrations.Should().HaveCount(2);
        registrations.Should().ContainSingle(registration => registration.Lane == MessageLane.Bus);
        registrations.Should().ContainSingle(registration => registration.Lane == MessageLane.Queue);
        registrations
            .Should()
            .OnlyContain(registration => registration.Consumers.Single().ConsumerType == typeof(OrderPlacedHandler));
    }

    [Fact]
    public void should_support_publisher_only_message_name_registration()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Bus.ForMessage<OrderPlaced>(message => message.MessageName("orders.placed"));
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        var registry = provider.GetDrainedConsumerRegistry();
        registry.GetAll().Should().BeEmpty();
        registry.TryGetRawMessageName(typeof(OrderPlaced), MessageLane.Bus, out var messageName).Should().BeTrue();
        messageName.Should().Be("orders.placed");
    }

    [Fact]
    public void should_apply_consumer_configuration_to_discovered_metadata()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Queue.ForMessage<OrderPlaced>(message =>
                message
                    .MessageName("orders.placed")
                    .Consumer<OrderPlacedHandler>(consumer =>
                        consumer.Group("orders").Concurrency(3).HandlerId("handler-1")
                    )
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        var metadata = provider.GetDrainedConsumerRegistry().GetAll().Single();
        metadata.MessageName.Should().Be("orders.placed");
        metadata.Group.Should().Be("orders");
        metadata.Concurrency.Should().Be(3);
        metadata.HandlerId.Should().Be("handler-1");
        metadata.IntentType.Should().Be(IntentType.Queue);
    }

    [Fact]
    public void should_keep_names_and_provider_configuration_independent_across_lanes()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Bus.ForMessage<OrderPlaced>(message =>
            {
                message.MessageName("orders.bus").Consumer<OrderPlacedHandler>(consumer => consumer.Group("bus"));
                ((IMessageProviderConfigBuilder<OrderPlaced>)message).SetMessageProviderConfig(
                    new LaneProviderConfig("bus")
                );
            });
            setup.Queue.ForMessage<OrderPlaced>(message =>
            {
                message.MessageName("orders.queue").Consumer<OrderPlacedHandler>(consumer => consumer.Group("queue"));
                ((IMessageProviderConfigBuilder<OrderPlaced>)message).SetMessageProviderConfig(
                    new LaneProviderConfig("queue")
                );
            });
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        // when
        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetServices<MessageRegistration>().ToArray();
        var metadata = provider.GetDrainedConsumerRegistry().GetAll();

        // then
        registrations
            .Single(registration => registration.Lane == MessageLane.Bus)
            .ProviderConfigs.Should()
            .ContainSingle()
            .Which.Value.Should()
            .Be(new LaneProviderConfig("bus"));
        registrations
            .Single(registration => registration.Lane == MessageLane.Queue)
            .ProviderConfigs.Should()
            .ContainSingle()
            .Which.Value.Should()
            .Be(new LaneProviderConfig("queue"));
        metadata.Should().ContainSingle(item => item.Lane == MessageLane.Bus && item.Group == "bus");
        metadata.Should().ContainSingle(item => item.Lane == MessageLane.Queue && item.Group == "queue");
    }

    [Fact]
    public void should_throw_when_concurrency_is_zero()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessMessaging(setup =>
                setup.Bus.ForMessage<OrderPlaced>(message =>
                    message.Consumer<OrderPlacedHandler>(consumer => consumer.Concurrency(0))
                )
            );

        // then
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Concurrency must be greater than 0*");
    }

    [Fact]
    public void should_build_default_scanned_consumer_as_bus_registration()
    {
        // given
        var builder = new ScannedConsumerBuilder(typeof(OrderPlacedHandler), MessageLane.Bus);

        // when
        var registration = builder.Build();

        // then
        registration.ConsumerType.Should().Be<OrderPlacedHandler>();
        registration.Lane.Should().Be(MessageLane.Bus);
        registration.IsAssemblyScan.Should().BeTrue();
        registration.Concurrency.Should().Be(1);
    }

    [Fact]
    public void should_build_configured_scanned_consumer_registration()
    {
        // given
        var builder = new ScannedConsumerBuilder(typeof(OrderPlacedHandler), MessageLane.Queue);

        // when
        builder.Group("orders").Concurrency(4).HandlerId("handler-1");
        var registration = builder.Build();

        // then
        registration.ConsumerType.Should().Be<OrderPlacedHandler>();
        registration.Lane.Should().Be(MessageLane.Queue);
        registration.Group.Should().Be("orders");
        registration.Concurrency.Should().Be(4);
        registration.HandlerId.Should().Be("handler-1");
    }

    [Fact]
    public void should_reject_invalid_scanned_consumer_builder_values()
    {
        // given
        var builder = new ScannedConsumerBuilder(typeof(OrderPlacedHandler), MessageLane.Bus);

        // then
        builder
            .Invoking(static x => x.Concurrency(0))
            .Should()
            .Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Concurrency must be greater than 0*");
        builder.Invoking(static x => x.Group(" ")).Should().Throw<ArgumentException>();
        builder.Invoking(static x => x.HandlerId(" ")).Should().Throw<ArgumentException>();
        builder.Invoking(static x => x.WithCircuitBreaker(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_mark_scanned_consumer_builder_as_skipped()
    {
        // given
        var builder = new ScannedConsumerBuilder(typeof(OrderPlacedHandler), MessageLane.Bus);

        // when
        var returned = builder.Skip();

        // then
        returned.Should().BeSameAs(builder);
        builder.IsSkipped.Should().BeTrue();
    }

    [Fact]
    public void should_merge_same_message_type_registrations()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Bus.ForMessage<OrderPlaced>(message =>
                message.MessageName("orders.placed").Consumer<OrderPlacedHandler>()
            );
            setup.Queue.ForMessage<OrderPlaced>(message =>
                message.MessageName("orders.placed").Consumer<OrderPlacedAnalyticsHandler>()
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        var consumers = provider.GetDrainedConsumerRegistry().GetAll();
        consumers.Should().HaveCount(2);
        consumers.Should().OnlyContain(consumer => consumer.MessageName == "orders.placed");
    }

    [Fact]
    public void should_reject_duplicate_same_consumer_registration()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessMessaging(static setup =>
            {
                setup.Bus.ForMessage<OrderPlaced>(message =>
                    message.MessageName("orders.placed").Consumer<OrderPlacedHandler>()
                );
                setup.Bus.ForMessage<OrderPlaced>(message => message.Consumer<OrderPlacedHandler>());
                setup.UseInMemory();
                setup.UseInMemoryStorage();
            });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*registered more than once on lane Bus*");
    }

    [Fact]
    public void should_reject_duplicate_registration_across_messaging_setup_calls()
    {
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(static setup => setup.Queue.ForMessage<OrderPlaced>(_ => { }));

        var act = () => services.AddHeadlessMessaging(static setup => setup.Queue.ForMessage<OrderPlaced>(_ => { }));

        act.Should().Throw<InvalidOperationException>().WithMessage("*registered more than once on lane Queue*");
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
                setup.Bus.ForMessage<OrderPlaced>(message =>
                    message.Consumer<OrderPlacedHandler>(consumer =>
                        consumer.WithCircuitBreaker(options => options.FailureThreshold = 3)
                    )
                );
                setup.Bus.ForMessage<OrderPlaced>(message =>
                    message.Consumer<OrderPlacedHandler>(consumer =>
                        consumer.WithCircuitBreaker(options => options.FailureThreshold = 5)
                    )
                );
                setup.UseInMemory();
                setup.UseInMemoryStorage();
            });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*registered more than once on lane Bus*");
    }

    [Fact]
    public void should_reject_different_consumers_with_same_name_group_and_intent()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessMessaging(static setup =>
            {
                setup.Bus.ForMessage<OrderPlaced>(message =>
                    message
                        .MessageName("orders.placed")
                        .Consumer<OrderPlacedHandler>(consumer => consumer.Group("orders"))
                );
                setup.Bus.ForMessage<OrderPlaced>(message =>
                    message.Consumer<OrderPlacedAnalyticsHandler>(consumer => consumer.Group("orders"))
                );
                setup.UseInMemory();
                setup.UseInMemoryStorage();
            });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*registered more than once on lane Bus*");
    }

    [Fact]
    public void should_reject_different_consumers_with_case_variant_names_in_same_group_and_intent()
    {
        // given — names differ only by case; dispatch matches names case-insensitively, so these are
        // the same identity and the competing-identity guard must fire (otherwise one consumer is
        // silently shadowed at dispatch).
        var services = new ServiceCollection();

        // when
        var act = () =>
        {
            services.AddHeadlessMessaging(static setup =>
            {
                setup.Bus.ForMessage<OrderPlaced>(message =>
                    message
                        .MessageName("orders.placed")
                        .Consumer<OrderPlacedHandler>(consumer => consumer.Group("orders"))
                );
                setup.Bus.ForMessage<OtherOrderPlaced>(message =>
                    message
                        .MessageName("Orders.Placed")
                        .Consumer<OtherOrderPlacedHandler>(consumer => consumer.Group("orders"))
                );
                setup.UseInMemory();
                setup.UseInMemoryStorage();
            });
            using var provider = services.BuildServiceProvider();
            provider.GetDrainedConsumerRegistry().GetAll();
        };

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Duplicate consumer registration detected for messageName/group identity*");
    }

    [Fact]
    public void should_reject_case_variant_explicit_names_for_same_message_type()
    {
        // given — same type, names differ only by case: the same logical name, not a conflict.
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessMessaging(static setup =>
            {
                setup.Bus.ForMessage<OrderPlaced>(message => message.MessageName("orders.placed"));
                setup.Bus.ForMessage<OrderPlaced>(message => message.MessageName("Orders.Placed"));
                setup.UseInMemory();
                setup.UseInMemoryStorage();
            });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*registered more than once on lane Bus*");
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
                setup.Bus.ForMessage<OrderPlaced>(message => message.MessageName("orders.placed"));
                setup.Bus.ForMessage<OrderPlaced>(message => message.MessageName("orders.placed.v2"));
                setup.UseInMemory();
                setup.UseInMemoryStorage();
            });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*registered more than once on lane Bus*");
    }

    [Fact]
    public async Task should_reject_cross_type_message_name_collisions_at_startup()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Bus.ForMessage<OrderPlaced>(message =>
                message.MessageName("orders.same").Consumer<OrderPlacedHandler>()
            );
            setup.Bus.ForMessage<OtherOrderPlaced>(message =>
                message.MessageName("orders.same").Consumer<OtherOrderPlacedHandler>()
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var act = () =>
            // ReSharper disable once AccessToDisposedClosure
            provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

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
            setup.Bus.ForMessage<OrderPlaced>(message =>
                message.MessageName("orders.same").Consumer<OrderPlacedHandler>()
            );
            setup.Bus.ForMessage<OtherOrderPlaced>(message =>
                message.MessageName("Orders.Same").Consumer<OtherOrderPlacedHandler>()
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*orders.same*OrderPlaced*OtherOrderPlaced*");
    }

    [Fact]
    public async Task should_reject_cross_type_collision_between_scanned_and_explicit_registration_at_startup()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Bus.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>();
            setup.Bus.ForMessage<OtherOrderPlaced>(message =>
                message.MessageName(nameof(OrderPlaced)).Consumer<OtherOrderPlacedHandler>()
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*OrderPlaced*OtherOrderPlaced*");
    }

    [Fact]
    public void should_scan_assembly_consumers_as_bus_registrations()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Bus.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>();
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        var consumers = provider.GetDrainedConsumerRegistry().GetAll();
        consumers.Should().Contain(consumer => consumer.ConsumerType == typeof(OrderPlacedHandler));
        consumers.Should().OnlyContain(consumer => consumer.IntentType == IntentType.Bus);
    }

    [Fact]
    public void should_configure_scanned_consumer_as_queue_registration()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Queue.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                (ctx, consumer) =>
                {
                    if (ctx.ConsumerType == typeof(OrderPlacedHandler))
                    {
                        consumer.Group("orders");
                    }
                }
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        var metadata = provider
            .GetDrainedConsumerRegistry()
            .GetAll()
            .Single(consumer => consumer.ConsumerType == typeof(OrderPlacedHandler));

        metadata.IntentType.Should().Be(IntentType.Queue);
        metadata.Group.Should().Be("orders");
    }

    [Fact]
    public void should_call_scanned_consumer_callback_once_per_discovered_consumer()
    {
        // given
        var services = new ServiceCollection();
        var callbackCounts = new Dictionary<(Type ConsumerType, Type MessageType), int>();

        // when
        services.AddHeadlessMessaging(setup =>
        {
            setup.Bus.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                (ctx, _) =>
                {
                    var key = (ctx.ConsumerType, ctx.MessageType);
                    callbackCounts[key] = callbackCounts.GetValueOrDefault(key) + 1;
                }
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        provider.GetDrainedConsumerRegistry().GetAll().Should().NotBeEmpty();
        callbackCounts
            .Should()
            .Contain(
                new KeyValuePair<(Type ConsumerType, Type MessageType), int>(
                    (typeof(OrderPlacedHandler), typeof(OrderPlaced)),
                    1
                )
            );
        callbackCounts
            .Should()
            .Contain(
                new KeyValuePair<(Type ConsumerType, Type MessageType), int>(
                    (typeof(OrderPlacedAnalyticsHandler), typeof(OrderPlaced)),
                    1
                )
            );
        callbackCounts
            .Should()
            .Contain(
                new KeyValuePair<(Type ConsumerType, Type MessageType), int>(
                    (typeof(OtherOrderPlacedHandler), typeof(OtherOrderPlaced)),
                    1
                )
            );
        callbackCounts.Values.Should().OnlyContain(count => count == 1);
    }

    [Fact]
    public void should_configure_mixed_scanned_consumers_independently()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Queue.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                (ctx, consumer) =>
                {
                    if (ctx.ConsumerType == typeof(OrderPlacedHandler))
                    {
                        consumer.Group("orders");
                    }
                }
            );
            setup.Bus.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                (ctx, consumer) =>
                {
                    if (ctx.ConsumerType == typeof(OrderPlacedAnalyticsHandler))
                    {
                        consumer.Group("analytics");
                    }
                }
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        var consumers = provider.GetDrainedConsumerRegistry().GetAll();
        consumers
            .Should()
            .Contain(consumer =>
                consumer.ConsumerType == typeof(OrderPlacedHandler)
                && consumer.IntentType == IntentType.Queue
                && consumer.Group == "orders"
            );
        consumers
            .Should()
            .Contain(consumer =>
                consumer.ConsumerType == typeof(OrderPlacedAnalyticsHandler)
                && consumer.IntentType == IntentType.Bus
                && consumer.Group == "analytics"
            );
    }

    [Fact]
    public void should_skip_scanned_consumer_from_registry_and_di()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Bus.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                (ctx, consumer) =>
                {
                    if (ctx.ConsumerType == typeof(OrderPlacedHandler))
                    {
                        consumer.Skip();
                    }
                }
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        var consumers = provider.GetDrainedConsumerRegistry().GetAll();
        consumers.Should().NotContain(consumer => consumer.ConsumerType == typeof(OrderPlacedHandler));

        provider.GetServices<IConsume<OrderPlaced>>().Should().NotContain(consumer => consumer is OrderPlacedHandler);

        consumers.Should().Contain(consumer => consumer.ConsumerType == typeof(OrderPlacedAnalyticsHandler));
        provider
            .GetServices<IConsume<OrderPlaced>>()
            .Should()
            .Contain(consumer => consumer is OrderPlacedAnalyticsHandler);
    }

    [Fact]
    public void should_skip_consumer_even_when_other_config_was_applied_before_skip()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Queue.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                (ctx, consumer) =>
                {
                    if (ctx.ConsumerType == typeof(OrderPlacedHandler))
                    {
                        consumer.Group("orders").Skip();
                    }
                }
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetDrainedConsumerRegistry()
            .GetAll()
            .Should()
            .NotContain(consumer => consumer.ConsumerType == typeof(OrderPlacedHandler));

        provider.GetServices<IConsume<OrderPlaced>>().Should().NotContain(consumer => consumer is OrderPlacedHandler);
    }

    [Fact]
    public void should_configure_scanned_consumer_handler_id_end_to_end()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Bus.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                (ctx, consumer) =>
                {
                    if (ctx.ConsumerType == typeof(OrderPlacedHandler))
                    {
                        consumer.HandlerId("handler-1");
                    }
                }
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetDrainedConsumerRegistry()
            .GetAll()
            .Single(consumer => consumer.ConsumerType == typeof(OrderPlacedHandler))
            .HandlerId.Should()
            .Be("handler-1");
    }

    [Fact]
    public void should_keep_untouched_scanned_consumer_equivalent_to_no_arg_scan()
    {
        // given
        var noArgServices = new ServiceCollection();
        var callbackServices = new ServiceCollection();

        // when
        noArgServices.AddHeadlessMessaging(static setup =>
        {
            setup.Bus.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>();
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });
        callbackServices.AddHeadlessMessaging(static setup =>
        {
            setup.Bus.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(static (_, _) => { });
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var noArgProvider = noArgServices.BuildServiceProvider();
        using var callbackProvider = callbackServices.BuildServiceProvider();

        // then
        var noArg = noArgProvider
            .GetDrainedConsumerRegistry()
            .GetAll()
            .Single(consumer => consumer.ConsumerType == typeof(OrderPlacedHandler));
        var callback = callbackProvider
            .GetDrainedConsumerRegistry()
            .GetAll()
            .Single(consumer => consumer.ConsumerType == typeof(OrderPlacedHandler));

        callback.IntentType.Should().Be(IntentType.Bus);
        callback.Concurrency.Should().Be(1);
        callback.Should().BeEquivalentTo(noArg);
    }

    [Fact]
    public void should_suppress_only_the_same_lane_scan_when_consumer_is_explicit()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Bus.ForMessage<OrderPlaced>(message => message.Consumer<OrderPlacedHandler>());
            setup.Bus.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>();
            setup.Queue.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>();
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        var registrations = provider
            .GetDrainedConsumerRegistry()
            .GetAll()
            .Where(consumer => consumer.ConsumerType == typeof(OrderPlacedHandler))
            .ToList();

        registrations.Should().HaveCount(2);
        registrations.Should().ContainSingle(registration => registration.Lane == MessageLane.Bus);
        registrations.Should().ContainSingle(registration => registration.Lane == MessageLane.Queue);
    }

    [Fact]
    public void should_not_let_configured_scan_override_explicit_consumer_registration()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Bus.ForMessage<OrderPlaced>(message => message.Consumer<OrderPlacedHandler>());
            setup.Bus.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                (ctx, consumer) =>
                {
                    if (ctx.ConsumerType == typeof(OrderPlacedHandler))
                    {
                        consumer.Group("ignored");
                    }
                }
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        var metadata = provider
            .GetDrainedConsumerRegistry()
            .GetAll()
            .Single(consumer => consumer.ConsumerType == typeof(OrderPlacedHandler));

        metadata.IntentType.Should().Be(IntentType.Bus);
        metadata.Group.Should().NotBe("ignored");
    }

    [Fact]
    public void should_reject_invalid_scanned_consumer_callback_configuration()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessMessaging(static setup =>
                setup.Bus.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                    (ctx, consumer) =>
                    {
                        if (ctx.ConsumerType == typeof(OrderPlacedHandler))
                        {
                            consumer.Concurrency(0);
                        }
                    }
                )
            );

        // then
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Concurrency must be greater than 0*");
    }

    [Fact]
    public void should_apply_scanned_consumer_circuit_breaker_override()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Bus.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                (ctx, consumer) =>
                {
                    if (ctx.ConsumerType == typeof(OrderPlacedHandler))
                    {
                        consumer.Group("orders").WithCircuitBreaker(options => options.FailureThreshold = 3);
                    }
                }
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        provider.GetDrainedConsumerRegistry();
        var circuitBreakers = provider.GetRequiredService<ConsumerCircuitBreakerRegistry>();
        circuitBreakers.TryGet($"{IntentType.Bus:D}:orders", out var options).Should().BeTrue();
        options!.FailureThreshold.Should().Be(3);
    }

    [Fact]
    public void should_apply_scanned_consumer_circuit_breaker_override_for_queue_consumer()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Queue.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                (ctx, consumer) =>
                {
                    if (ctx.ConsumerType == typeof(OrderPlacedHandler))
                    {
                        consumer.Group("orders").WithCircuitBreaker(options => options.FailureThreshold = 3);
                    }
                }
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        provider.GetDrainedConsumerRegistry();
        var circuitBreakers = provider.GetRequiredService<ConsumerCircuitBreakerRegistry>();
        circuitBreakers.TryGet($"{IntentType.Queue:D}:orders", out var options).Should().BeTrue();
        options!.FailureThreshold.Should().Be(3);
    }

    [Fact]
    public void should_reject_competing_scanned_consumers_with_same_handler_identity()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
        {
            services.AddHeadlessMessaging(static setup =>
            {
                setup.Bus.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                    (ctx, consumer) =>
                    {
                        if (
                            ctx.ConsumerType == typeof(OrderPlacedHandler)
                            || ctx.ConsumerType == typeof(OrderPlacedAnalyticsHandler)
                        )
                        {
                            consumer.Group("orders").HandlerId("handler-1");
                        }
                    }
                );
                setup.UseInMemory();
                setup.UseInMemoryStorage();
            });
            using var provider = services.BuildServiceProvider();
            provider.GetDrainedConsumerRegistry().GetAll();
        };

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Duplicate consumer registration detected for messageName/group identity*");
    }

    [Fact]
    public void should_keep_configured_rescan_idempotent()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Queue.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                (ctx, consumer) =>
                {
                    if (ctx.ConsumerType == typeof(OrderPlacedHandler))
                    {
                        consumer.Group("orders").Concurrency(2).HandlerId("handler-1");
                    }
                }
            );
            setup.Queue.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                (ctx, consumer) =>
                {
                    if (ctx.ConsumerType == typeof(OrderPlacedHandler))
                    {
                        consumer.Group("orders").Concurrency(2).HandlerId("handler-1");
                    }
                }
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetDrainedConsumerRegistry()
            .GetAll()
            .Where(consumer => consumer.ConsumerType == typeof(OrderPlacedHandler))
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void should_reject_same_scanned_consumer_configured_differently_across_two_scans()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
        {
            services.AddHeadlessMessaging(static setup =>
            {
                setup.Queue.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                    (ctx, consumer) =>
                    {
                        if (ctx.ConsumerType == typeof(OrderPlacedHandler))
                        {
                            consumer.Group("orders").Concurrency(2);
                        }
                    }
                );
                setup.Queue.ForConsumersFromAssemblyContaining<ForMessageRegistrationTests>(
                    (ctx, consumer) =>
                    {
                        if (ctx.ConsumerType == typeof(OrderPlacedHandler))
                        {
                            consumer.Group("orders").Concurrency(4);
                        }
                    }
                );
                setup.UseInMemory();
                setup.UseInMemoryStorage();
            });
            using var provider = services.BuildServiceProvider();
            provider.GetDrainedConsumerRegistry().GetAll();
        };

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*conflicting settings*");
    }

    [Fact]
    public void should_resolve_explicit_message_name_for_publish_before_consumer_drain()
    {
        // given — names are registered eagerly at ForMessage<T>(...) time, so a publish that races ahead
        // of the startup consumer drain (e.g. an IHostedService publishing in StartAsync) must still see
        // the explicit name. Before Plan B the factory fell back to the convention name and cached it
        // permanently, silently diverging publish from subscribe.
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(static setup =>
        {
            setup.Bus.ForMessage<OrderPlaced>(message =>
                message.MessageName("orders.placed").Consumer<OrderPlacedHandler>()
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();

        // when — resolve and publish WITHOUT draining the registry first
        var factory = provider.GetRequiredService<IMessagePublishRequestFactory>();
        var prepared = factory.Create(new OrderPlaced());

        // then — the eager name resolved even though the consumer drain has not run
        prepared.MessageName.Should().Be("orders.placed");
        provider.GetRequiredService<ConsumerRegistry>().HasCompletedMessageRegistrationDrain.Should().BeFalse();
    }

    private sealed record OrderPlaced;

    private sealed record OtherOrderPlaced;

    private sealed record LaneProviderConfig(string Value);

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
