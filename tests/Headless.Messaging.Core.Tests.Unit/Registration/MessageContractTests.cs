// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Registration;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

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
            messaging.AddConsumerContribution<OrderPlaced, OrderPlacedBusHandler>(
                MessageLane.Bus,
                "billing.order-placed",
                messageContractVersion: "1"
            );
            messaging.AddConsumerContribution<OrderPlaced, OrderPlacedQueueHandler>(
                MessageLane.Queue,
                "shipping.order-placed",
                messageContractVersion: "1"
            );
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
    public void should_reject_a_contract_for_a_message_already_declared_through_for_message()
    {
        // given
        var services = new ServiceCollection();
        _AddMessagingHost(
            services,
            setup => setup.Bus.ForMessage<OrderPlaced>(message => message.Contract(_OrderPlacedName))
        );

        // when
        var declare = () =>
            services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>(_OrderPlacedName, "1"));

        // then
        declare.Should().Throw<InvalidOperationException>().WithMessage("*already declared on lane Bus*ForMessage*");
    }

    [Fact]
    public void should_reject_for_message_for_a_message_already_declared_by_a_contract()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>(_OrderPlacedName, "1"));

        // when
        var declare = () =>
            services.ConfigureMessaging(messaging =>
                messaging.Queue.ForMessage<OrderPlaced>(message => message.Contract(_OrderPlacedName))
            );

        // then
        declare.Should().Throw<InvalidOperationException>().WithMessage("*more than once on lane Queue*Message<T>*");
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

    public sealed class OrderPlacedBusHandler : IConsume<OrderPlaced>
    {
        public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class OrderPlacedQueueHandler : IConsume<OrderPlaced>
    {
        public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
