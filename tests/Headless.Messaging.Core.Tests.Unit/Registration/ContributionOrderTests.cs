// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Registration;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Registration;

public sealed class ContributionOrderTests : TestBase
{
    private const string _BeforeIdentity = "tests.contributions.before";
    private const string _AfterIdentity = "tests.contributions.after";

    [Fact]
    public void should_apply_contributions_added_before_and_after_add_headless_messaging()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.ConfigureMessaging(messaging =>
            messaging.Bus.ForMessage<ContributedBusMessage>(message =>
                message
                    .Contract("tests.contributions.bus")
                    .Consumer<ContributedBusHandler>(consumer => consumer.ConsumerIdentity(_BeforeIdentity))
            )
        );
        _AddMessagingHost(services);
        services.ConfigureMessaging(messaging =>
            messaging.Queue.ForMessage<ContributedQueueMessage>(message =>
                message
                    .Contract("tests.contributions.queue")
                    .Consumer<ContributedQueueHandler>(consumer => consumer.ConsumerIdentity(_AfterIdentity))
            )
        );
        using var provider = services.BuildServiceProvider();

        // then
        var consumers = provider.GetDrainedConsumerRegistry().GetAll();
        consumers
            .Select(consumer => (consumer.ConsumerType, consumer.Lane, consumer.MessageName, consumer.ConsumerIdentity))
            .Should()
            .BeEquivalentTo([
                (typeof(ContributedBusHandler), MessageLane.Bus, "tests.contributions.bus", _BeforeIdentity),
                (typeof(ContributedQueueHandler), MessageLane.Queue, "tests.contributions.queue", _AfterIdentity),
            ]);
        provider.GetRequiredService<IConsume<ContributedBusMessage>>().Should().BeOfType<ContributedBusHandler>();
        provider.GetRequiredService<IConsume<ContributedQueueMessage>>().Should().BeOfType<ContributedQueueHandler>();
    }

    [Fact]
    public void should_drain_contributions_in_registration_order()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging =>
            messaging.Queue.ForMessage<ContributedQueueMessage>(message =>
                message
                    .Contract("tests.contributions.queue")
                    .Consumer<ContributedQueueHandler>(consumer => consumer.ConsumerIdentity(_AfterIdentity))
            )
        );
        _AddMessagingHost(services);
        services.ConfigureMessaging(messaging =>
            messaging.Bus.ForMessage<ContributedBusMessage>(message =>
                message
                    .Contract("tests.contributions.bus")
                    .Consumer<ContributedBusHandler>(consumer => consumer.ConsumerIdentity(_BeforeIdentity))
            )
        );
        using var provider = services.BuildServiceProvider();

        // when
        var consumers = provider.GetDrainedConsumerRegistry().GetAll();

        // then
        consumers
            .Select(consumer => consumer.ConsumerType)
            .Should()
            .Equal(typeof(ContributedQueueHandler), typeof(ContributedBusHandler));
    }

    [Fact]
    public void should_merge_identical_contributions_for_one_handler()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.ConfigureMessaging(messaging => _ContributeBusHandler(messaging, concurrency: 2));
        _AddMessagingHost(services);
        services.ConfigureMessaging(messaging => _ContributeBusHandler(messaging, concurrency: 2));
        using var provider = services.BuildServiceProvider();

        // then
        var consumer = provider.GetDrainedConsumerRegistry().GetAll().Should().ContainSingle().Subject;
        consumer.ConsumerType.Should().Be<ContributedBusHandler>();
        consumer.ConsumerIdentity.Should().Be(_BeforeIdentity);
        consumer.Concurrency.Should().Be(2);
        services.Count(descriptor => descriptor.ServiceType == typeof(IConsume<ContributedBusMessage>)).Should().Be(1);
    }

    [Fact]
    public void should_fail_at_startup_naming_both_contributions_when_one_handler_conflicts()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging => _ContributeBusHandler(messaging, concurrency: 2));
        _AddMessagingHost(services);
        services.ConfigureMessaging(messaging => _ContributeBusHandler(messaging, concurrency: 7));
        using var provider = services.BuildServiceProvider();

        // when
        var drain = () => provider.GetDrainedConsumerRegistry();

        // then
        drain
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ContributedBusHandler)}*conflicting*concurrency 2*concurrency 7*");
    }

    [Fact]
    public void should_fail_at_startup_naming_both_consumers_when_contributions_share_an_identity()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureMessaging(messaging =>
            messaging.AddConsumerContribution<ContributedBusMessage, ContributedBusHandler>(
                MessageLane.Bus,
                _BeforeIdentity,
                messageContractVersion: "1",
                messageName: "tests.contributions.bus"
            )
        );
        _AddMessagingHost(services);
        services.ConfigureMessaging(messaging =>
            messaging.AddConsumerContribution<ContributedBusMessage, OtherContributedBusHandler>(
                MessageLane.Bus,
                _BeforeIdentity,
                messageContractVersion: "1",
                messageName: "tests.contributions.bus"
            )
        );
        using var provider = services.BuildServiceProvider();

        // when
        var drain = () => provider.GetDrainedConsumerRegistry();

        // then
        drain
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{_BeforeIdentity}'*{nameof(ContributedBusHandler)}*{nameof(OtherContributedBusHandler)}*");
    }

    [Fact]
    public async Task should_keep_contributions_inert_when_the_host_never_adds_messaging()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.ConfigureMessaging(messaging => _ContributeBusHandler(messaging, concurrency: 1));

        // when
        using var host = builder.Build();
        await host.StartAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        host.Services.GetService<IBootstrapper>().Should().BeNull();
        host.Services.GetService<IBus>().Should().BeNull();
    }

    [Fact]
    public void should_register_contributions_without_adding_messaging()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.ConfigureMessaging(messaging => _ContributeBusHandler(messaging, concurrency: 1));

        // then
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IBootstrapper));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IBus));
    }

    [Fact]
    public void should_reject_a_null_contribution()
    {
        var services = new ServiceCollection();

        var configure = () => services.ConfigureMessaging(null!);

        configure.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_still_reject_a_message_type_registered_twice_on_one_lane_across_setup_calls()
    {
        // given
        var services = new ServiceCollection();
        _AddMessagingHost(services, messaging => _ForBusMessage(messaging.Bus));

        // when
        var addAgain = () => services.AddHeadlessMessaging(setup => _ForBusMessage(setup.Bus));

        // then
        addAgain.Should().Throw<InvalidOperationException>().WithMessage("*registered more than once on lane Bus*");
    }

    private static void _ContributeBusHandler(MessagingContributionBuilder messaging, byte concurrency)
    {
        messaging.AddConsumerContribution<ContributedBusMessage, ContributedBusHandler>(
            MessageLane.Bus,
            _BeforeIdentity,
            messageContractVersion: "1",
            messageName: "tests.contributions.bus",
            concurrency: concurrency
        );
    }

    private static void _ForBusMessage(IBusRegistrationBuilder bus)
    {
        bus.ForMessage<ContributedBusMessage>(message =>
            message
                .Contract("tests.contributions.bus")
                .Consumer<ContributedBusHandler>(consumer => consumer.ConsumerIdentity(_BeforeIdentity))
        );
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

    public sealed record ContributedBusMessage(string Id);

    public sealed record ContributedQueueMessage(string Id);

    public sealed class ContributedBusHandler : IConsume<ContributedBusMessage>
    {
        public ValueTask ConsumeAsync(
            ConsumeContext<ContributedBusMessage> context,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }

    public sealed class OtherContributedBusHandler : IConsume<ContributedBusMessage>
    {
        public ValueTask ConsumeAsync(
            ConsumeContext<ContributedBusMessage> context,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }

    public sealed class ContributedQueueHandler : IConsume<ContributedQueueMessage>
    {
        public ValueTask ConsumeAsync(
            ConsumeContext<ContributedQueueMessage> context,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }
}
