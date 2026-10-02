// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Registration;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests.Registration;

/// <summary>
/// <c>AddMessageContract</c> is the abstractions-level form of <c>Message&lt;T&gt;(name, version)</c>, so a library
/// declares its contracts without referencing Messaging.Core and the declaration behaves exactly like one made through
/// <c>ConfigureMessaging</c>.
/// </summary>
public sealed class MessageContractDeclarationTests : TestBase
{
    private const string _OrderPlacedName = "orders.placed";

    private const string _OrderShippedName = "orders.shipped";

    [Fact]
    public void should_resolve_the_declared_name_on_both_lanes()
    {
        // given
        var services = new ServiceCollection();
        services.AddMessageContract<OrderPlaced>(_OrderPlacedName, "2");
        _AddMessagingHost(services);
        services.ConfigureMessaging(messaging =>
        {
            messaging.AddConsumer<OrderPlacedBusHandler>();
            messaging.AddConsumer<OrderPlacedQueueHandler>();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var consumers = provider.GetRequiredService<ConsumerRegistry>().GetAll();
        var routes = provider.GetRequiredService<IMessageMetadataRegistry>().GetAll();

        // then
        consumers
            .Select(consumer => (consumer.Lane, consumer.MessageName, consumer.MessageContractVersion))
            .Should()
            .BeEquivalentTo([(MessageLane.Bus, _OrderPlacedName, "2"), (MessageLane.Queue, _OrderPlacedName, "2")]);
        routes
            .Select(route => (route.Route.Lane, route.Route.MessageName, route.ContractVersion))
            .Should()
            .BeEquivalentTo([(MessageLane.Bus, _OrderPlacedName, "2"), (MessageLane.Queue, _OrderPlacedName, "2")]);
    }

    [Fact]
    public void should_apply_declarations_made_before_and_after_messaging_is_registered()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMessageContract<OrderPlaced>(_OrderPlacedName);
        _AddMessagingHost(services);
        services.AddMessageContract<OrderShipped>(_OrderShippedName);
        using var provider = services.BuildServiceProvider();

        // then - resolved before startup, as a publish from a hosted service's StartAsync would be
        var factory = provider.GetRequiredService<IMessagePublishRequestFactory>();
        factory.Create(new OrderPlaced("order-1")).MessageName.Should().Be(_OrderPlacedName);
        factory.Create(new OrderShipped("order-1")).MessageName.Should().Be(_OrderShippedName);
        provider
            .GetRequiredService<IMessageMetadataRegistry>()
            .GetAll()
            .Select(route => (route.Route.Lane, route.Route.MessageName))
            .Should()
            .BeEquivalentTo([
                (MessageLane.Bus, _OrderPlacedName),
                (MessageLane.Queue, _OrderPlacedName),
                (MessageLane.Bus, _OrderShippedName),
                (MessageLane.Queue, _OrderShippedName),
            ]);
    }

    [Fact]
    public void should_fold_the_declarations_into_a_separate_registry_for_each_provider()
    {
        // given - one collection may build several providers, as a test host and its application do
        var services = new ServiceCollection();
        services.AddMessageContract<OrderPlaced>(_OrderPlacedName);
        _AddMessagingHost(services);
        using var first = services.BuildServiceProvider();
        using var second = services.BuildServiceProvider();

        // when
        var firstRegistry = first.GetRequiredService<ConsumerRegistry>();
        var secondRegistry = second.GetRequiredService<ConsumerRegistry>();

        // then
        firstRegistry.Should().NotBeSameAs(secondRegistry);
        secondRegistry.Contracts.Should().ContainSingle().Which.Name.Should().Be(_OrderPlacedName);
    }

    [Fact]
    public void should_merge_with_an_identical_declaration_made_earlier_through_configure_messaging()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>(_OrderPlacedName, "1"));

        services.AddMessageContract<OrderPlaced>(_OrderPlacedName, "1");
        services.AddMessageContract<OrderPlaced>(_OrderPlacedName, "1");
        _AddMessagingHost(services);
        using var provider = services.BuildServiceProvider();

        // when
        var contracts = provider.GetRequiredService<ConsumerRegistry>().Contracts;

        // then
        contracts.Should().ContainSingle();
    }

    [Fact]
    public void should_merge_with_an_identical_declaration_made_later_through_configure_messaging()
    {
        // given
        var services = new ServiceCollection();
        services.AddMessageContract<OrderPlaced>(_OrderPlacedName, "1");

        _AddMessagingHost(services);
        services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>(_OrderPlacedName, "1"));
        using var provider = services.BuildServiceProvider();

        // when
        var contracts = provider.GetRequiredService<ConsumerRegistry>().Contracts;

        // then
        contracts.Should().ContainSingle();
    }

    [Fact]
    public void should_fail_naming_both_when_it_conflicts_with_a_configure_messaging_declaration()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>(_OrderPlacedName, "1"));
        services.AddMessageContract<OrderPlaced>("orders.order-placed", "1");
        _AddMessagingHost(services);
        using var provider = services.BuildServiceProvider();

        // when
        var build = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                $"*{nameof(OrderPlaced)}*conflicting*\"{_OrderPlacedName}\", \"1\"*\"orders.order-placed\", \"1\"*"
            );
    }

    [Fact]
    public void should_fail_naming_both_when_a_later_configure_messaging_declaration_adds_lane_settings()
    {
        // given
        var services = new ServiceCollection();
        services.AddMessageContract<OrderPlaced>(_OrderPlacedName, "1");
        _AddMessagingHost(services);
        services.ConfigureMessaging(messaging =>
            messaging
                .Message<OrderPlaced>(_OrderPlacedName, "1")
                .OnBus(bus => bus.WithDeliveryMode(DeliveryMode.Direct))
        );
        using var provider = services.BuildServiceProvider();

        // when
        var build = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*conflicting*Bus: defaults*Bus: delivery mode Direct*");
    }

    [Fact]
    public void should_fail_naming_both_when_declarations_recorded_before_messaging_conflict()
    {
        // given
        var services = new ServiceCollection();
        services.AddMessageContract<OrderPlaced>(_OrderPlacedName, "1");
        services.AddMessageContract<OrderPlaced>(_OrderPlacedName, "2");
        _AddMessagingHost(services);
        using var provider = services.BuildServiceProvider();

        // when
        var build = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*conflicting*\"{_OrderPlacedName}\", \"1\"*\"{_OrderPlacedName}\", \"2\"*");
    }

    [Fact]
    public void should_stay_inert_when_messaging_is_not_registered()
    {
        // given
        var services = new ServiceCollection();

        // when - even conflicting declarations, which only messaging judges
        services.AddMessageContract<OrderPlaced>(_OrderPlacedName, "1");
        services.AddMessageContract<OrderPlaced>(_OrderPlacedName, "2");

        // then
        services
            .Should()
            .OnlyContain(descriptor => descriptor.ServiceType == typeof(MessageDeclaration))
            .And.HaveCount(2);
    }

    [Theory]
    [InlineData(" ", "1")]
    [InlineData(_OrderPlacedName, " ")]
    public void should_reject_a_blank_name_or_version(string name, string version)
    {
        // given
        var services = new ServiceCollection();

        // when
        var declare = () => services.AddMessageContract<OrderPlaced>(name, version);

        // then
        declare.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_a_version_messaging_cannot_store_when_the_registry_is_built()
    {
        // given
        var services = new ServiceCollection();
        _AddMessagingHost(services);
        services.AddMessageContract<OrderPlaced>(_OrderPlacedName, "v 1");
        using var provider = services.BuildServiceProvider();

        // when
        var build = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        build.Should().Throw<ArgumentException>().WithMessage("*whitespace*");
    }

    private static void _AddMessagingHost(IServiceCollection services)
    {
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
        });
    }

    public sealed record OrderPlaced(string OrderId);

    public sealed record OrderShipped(string OrderId);

    [BusConsumer("billing.declared-order-placed")]
    public sealed class OrderPlacedBusHandler : IConsume<OrderPlaced>
    {
        public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    [QueueConsumer("shipping.declared-order-placed")]
    public sealed class OrderPlacedQueueHandler : IConsume<OrderPlaced>
    {
        public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
