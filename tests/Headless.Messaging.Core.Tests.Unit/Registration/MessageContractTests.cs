// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Registration;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests.Registration;

public sealed class MessageContractTests : TestBase
{
    private const string _OrderPlacedName = "orders.placed";

    [Fact]
    public void should_resolve_the_contract_name_on_both_lanes()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>(_OrderPlacedName, "1"));
        _AddMessagingHost(services);
        services.ConfigureMessaging(messaging =>
        {
            messaging.AddConsumer<OrderPlacedBusHandler>();
            messaging.AddConsumer<OrderPlacedQueueHandler>();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var consumers = provider.GetDrainedConsumerRegistry().GetAll();
        var routes = provider.GetRequiredService<IMessageMetadataRegistry>().GetAll();

        // then
        consumers
            .Select(consumer => (consumer.Lane, consumer.MessageName))
            .Should()
            .BeEquivalentTo([(MessageLane.Bus, _OrderPlacedName), (MessageLane.Queue, _OrderPlacedName)]);
        routes
            .Select(route => (route.Route.Lane, route.Route.MessageName, route.ContractVersion))
            .Should()
            .BeEquivalentTo([(MessageLane.Bus, _OrderPlacedName, "1"), (MessageLane.Queue, _OrderPlacedName, "1")]);
    }

    [Fact]
    public void should_merge_identical_declarations_from_several_modules()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.ConfigureMessaging(_DeclareOrderContracts);
        _AddMessagingHost(services);
        services.ConfigureMessaging(_DeclareOrderContracts);
        using var provider = services.BuildServiceProvider();

        // then
        var routes = provider.GetRequiredService<IMessageMetadataRegistry>().GetAll();
        routes
            .Select(route => (route.Route.Lane, route.Route.MessageName, route.ContractVersion))
            .Should()
            .BeEquivalentTo([(MessageLane.Bus, _OrderPlacedName, "2"), (MessageLane.Queue, _OrderPlacedName, "2")]);
        routes.Should().OnlyContain(route => route.CorrelationSelector!(new OrderPlaced("order-7")) == "order-7");
        services.Count(descriptor => descriptor.ImplementationInstance is MessageContract).Should().Be(1);
    }

    [Fact]
    public void should_fail_naming_both_declarations_when_names_differ()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>(_OrderPlacedName, "1"));

        // when
        var declareAgain = () =>
            services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>("orders.order-placed", "1"));

        // then
        declareAgain
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                $"*{nameof(OrderPlaced)}*conflicting*\"{_OrderPlacedName}\", \"1\"*\"orders.order-placed\", \"1\"*"
            );
    }

    [Fact]
    public void should_fail_naming_both_declarations_when_versions_differ()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>(_OrderPlacedName, "1"));

        // when
        var declareAgain = () =>
            services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>(_OrderPlacedName, "2"));

        // then
        declareAgain
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*conflicting*\"{_OrderPlacedName}\", \"1\"*\"{_OrderPlacedName}\", \"2\"*");
    }

    [Fact]
    public void should_fail_when_declarations_differ_only_in_lane_settings()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>(_OrderPlacedName, "1"));

        // when
        var declareAgain = () =>
            services.ConfigureMessaging(messaging =>
                messaging
                    .Message<OrderPlaced>(_OrderPlacedName, "1")
                    .OnBus(bus => bus.WithDeliveryMode(DeliveryMode.Direct))
            );

        // then
        declareAgain
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*conflicting*Bus: defaults*Bus: delivery mode Direct*");
    }

    [Fact]
    public void should_apply_a_provider_setting_to_the_lane_that_declares_it_only()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging =>
            messaging
                .Message<OrderPlaced>(_OrderPlacedName, "1")
                .OnBus(bus => _SetProviderConfig(bus, new PartitionSetting("customer")))
        );
        _AddMessagingHost(services);
        using var provider = services.BuildServiceProvider();

        // when
        var routes = provider.GetRequiredService<IMessageMetadataRegistry>().GetAll();

        // then
        routes
            .Single(route => route.Route.Lane == MessageLane.Bus)
            .ProviderConfigs.Should()
            .ContainSingle()
            .Which.Value.Should()
            .Be(new PartitionSetting("customer"));
        routes.Single(route => route.Route.Lane == MessageLane.Queue).ProviderConfigs.Should().BeEmpty();
    }

    [Fact]
    public void should_merge_declarations_that_carry_equal_provider_settings()
    {
        // given
        var services = new ServiceCollection();
        void declare(MessagingContributionBuilder messaging) =>
            messaging
                .Message<OrderPlaced>(_OrderPlacedName, "1")
                .OnQueue(queue => _SetProviderConfig(queue, new PartitionSetting("customer")));

        // when
        services.ConfigureMessaging(declare);
        services.ConfigureMessaging(declare);

        // then
        services.Count(descriptor => descriptor.ImplementationInstance is MessageContract).Should().Be(1);
    }

    [Fact]
    public void should_fail_when_declarations_differ_only_in_provider_settings()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging =>
            messaging
                .Message<OrderPlaced>(_OrderPlacedName, "1")
                .OnQueue(queue => _SetProviderConfig(queue, new PartitionSetting("customer")))
        );

        // when
        var declareAgain = () =>
            services.ConfigureMessaging(messaging =>
                messaging
                    .Message<OrderPlaced>(_OrderPlacedName, "1")
                    .OnQueue(queue => _SetProviderConfig(queue, new PartitionSetting("region")))
            );

        // then
        declareAgain
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*conflicting*Queue: provider settings {nameof(PartitionSetting)}*");
    }

    [Fact]
    public void should_apply_queue_settings_to_the_queue_route_only()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging =>
            messaging
                .Message<OrderPlaced>(_OrderPlacedName, "1")
                .OnQueue(queue => queue.RequireRoutingAffinity().WithDeliveryMode(DeliveryMode.Direct))
        );
        _AddMessagingHost(services);
        using var provider = services.BuildServiceProvider();

        // when
        var routes = provider.GetRequiredService<IMessageMetadataRegistry>().GetAll();

        // then
        routes
            .Select(route => (route.Route.Lane, route.RequiresRoutingAffinity))
            .Should()
            .BeEquivalentTo([(MessageLane.Bus, false), (MessageLane.Queue, true)]);
        _DeliveryModes(services)
            .Should()
            .BeEquivalentTo([(MessageLane.Bus, (DeliveryMode?)null), (MessageLane.Queue, DeliveryMode.Direct)]);
    }

    [Fact]
    public void should_apply_bus_settings_to_the_bus_route_only()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging =>
            messaging
                .Message<OrderPlaced>(_OrderPlacedName, "1")
                .OnBus(bus => bus.RequireRoutingAffinity().WithDeliveryMode(DeliveryMode.Direct))
        );
        _AddMessagingHost(services);
        using var provider = services.BuildServiceProvider();

        // when
        var routes = provider.GetRequiredService<IMessageMetadataRegistry>().GetAll();

        // then
        routes
            .Select(route => (route.Route.Lane, route.RequiresRoutingAffinity))
            .Should()
            .BeEquivalentTo([(MessageLane.Bus, true), (MessageLane.Queue, false)]);
        _DeliveryModes(services)
            .Should()
            .BeEquivalentTo([(MessageLane.Bus, DeliveryMode.Direct), (MessageLane.Queue, (DeliveryMode?)null)]);
    }

    [Fact]
    public void should_reject_configuring_a_contract_after_its_contribution_returns()
    {
        // given
        var services = new ServiceCollection();
        IMessageContractBuilder<OrderPlaced>? kept = null;
        services.ConfigureMessaging(messaging => kept = messaging.Message<OrderPlaced>(_OrderPlacedName, "1"));

        // when
        var configureLate = () => kept!.OnQueue(queue => queue.RequireRoutingAffinity());

        // then
        configureLate
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*inside the ConfigureMessaging callback*");
    }

    [Theory]
    [InlineData(" ", "1")]
    [InlineData(_OrderPlacedName, " ")]
    public void should_reject_an_invalid_name_or_version(string name, string version)
    {
        // given
        var services = new ServiceCollection();

        // when
        var declare = () => services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>(name, version));

        // then
        declare.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_resolve_the_contract_name_for_a_publish_before_the_consumer_drain()
    {
        // given — a publish that runs before startup drains the consumers, such as one from a hosted service's
        // StartAsync, must already see the declared name rather than cache the convention name.
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>(_OrderPlacedName));
        _AddMessagingHost(services);
        using var provider = services.BuildServiceProvider();

        // when
        var prepared = provider.GetRequiredService<IMessagePublishRequestFactory>().Create(new OrderPlaced("order-1"));

        // then
        prepared.MessageName.Should().Be(_OrderPlacedName);
        provider.GetRequiredService<ConsumerRegistry>().HasCompletedMessageRegistrationDrain.Should().BeFalse();
    }

    [Theory]
    [InlineData("orders.same")]
    [InlineData("Orders.Same")]
    public async Task should_reject_two_message_types_declaring_one_contract_name_at_startup(string otherName)
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<OrderPlaced>("orders.same");
            messaging.Message<OtherOrderPlaced>(otherName);
        });
        _AddMessagingHost(services);
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*OrderPlaced*OtherOrderPlaced*");
    }

    // A contracts package exposes one declaration method that every module calls, so each call passes the same cached
    // selector delegate and the declarations are identical.
    private static void _DeclareOrderContracts(MessagingContributionBuilder messaging)
    {
        messaging
            .Message<OrderPlaced>(_OrderPlacedName, "2")
            .CorrelateBy(static message => message.OrderId)
            .OnQueue(static queue => queue.RequireRoutingAffinity());
    }

    private static IEnumerable<(MessageLane Lane, DeliveryMode? DeliveryMode)> _DeliveryModes(
        IServiceCollection services
    )
    {
        return services
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<MessageRegistration>()
            .Where(static registration => registration.MessageType == typeof(OrderPlaced))
            .Select(static registration => (registration.Lane, registration.DeliveryMode));
    }

    // Provider packages reach a lane's route through this seam; a record stands in for their settings here.
    private static void _SetProviderConfig<TLane>(TLane lane, object config) =>
        ((IMessageProviderConfigBuilder<OrderPlaced>)lane!).SetMessageProviderConfig(config);

    private static void _AddMessagingHost(
        IServiceCollection services,
        Action<Headless.Messaging.Configuration.MessagingSetupBuilder>? configure = null
    )
    {
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
            configure?.Invoke(setup);
        });
    }

    public sealed record OrderPlaced(string OrderId);

    public sealed record OtherOrderPlaced(string OrderId);

    private sealed record PartitionSetting(string Key);

    [BusConsumer("billing.order-placed")]
    public sealed class OrderPlacedBusHandler : IConsume<OrderPlaced>
    {
        public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    [QueueConsumer("shipping.order-placed")]
    public sealed class OrderPlacedQueueHandler : IConsume<OrderPlaced>
    {
        public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
