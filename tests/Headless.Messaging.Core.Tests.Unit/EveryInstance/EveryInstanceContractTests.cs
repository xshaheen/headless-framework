// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Registration;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests.EveryInstance;

/// <summary>
/// Covers the contract pieces of every-instance delivery: the consumer client request, the per-process Bus name, the
/// transport capability flag, and the startup conflicts an every-instance consumer rejects.
/// </summary>
public sealed class EveryInstanceContractTests : TestBase
{
    private static readonly Guid _Instance = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    private static readonly BusNameRules _DotsAllowed = new(50, static c => c is '.' or '-' or '_');

    [Fact]
    public void should_default_a_request_to_a_competing_subscription()
    {
        var request = new ConsumerClientRequest("billing.invoices", 2, MessageLane.Queue);

        request.Kind.Should().Be(ConsumerSubscriptionKind.Competing);
        request.InstanceId.Should().BeEmpty();
    }

    [Fact]
    public void should_reject_an_every_instance_request_on_the_queue_lane()
    {
        var act = () =>
            new ConsumerClientRequest(
                "billing.invoices",
                1,
                MessageLane.Queue,
                ConsumerSubscriptionKind.EveryInstance,
                _Instance
            );

        act.Should().Throw<ArgumentException>().WithMessage("*Bus lane*");
    }

    [Fact]
    public void should_reject_an_every_instance_request_without_an_instance_id()
    {
        var act = () =>
            new ConsumerClientRequest("billing.prices", 1, MessageLane.Bus, ConsumerSubscriptionKind.EveryInstance);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_build_the_identity_name_for_a_competing_request_and_a_per_process_name_for_every_instance()
    {
        var competing = new ConsumerClientRequest("billing.prices", 1, MessageLane.Bus);
        var mine = new ConsumerClientRequest(
            "billing.prices",
            1,
            MessageLane.Bus,
            ConsumerSubscriptionKind.EveryInstance,
            _Instance
        );
        var theirs = mine with { };
        var other = new ConsumerClientRequest(
            "billing.prices",
            1,
            MessageLane.Bus,
            ConsumerSubscriptionKind.EveryInstance,
            Guid.NewGuid()
        );

        BusNameBuilder.Build(competing, _DotsAllowed).Should().Be("billing.prices");
        BusNameBuilder.Build(mine, _DotsAllowed).Should().Be(BusNameBuilder.Build(theirs, _DotsAllowed));
        BusNameBuilder.Build(mine, _DotsAllowed).Should().NotBe(BusNameBuilder.Build(other, _DotsAllowed));
        BusNameBuilder.Build(mine, _DotsAllowed).Length.Should().BeLessThanOrEqualTo(50);
        BusNameBuilder.Build(mine, _DotsAllowed).Should().StartWith("billing.prices");
    }

    [Fact]
    public void should_support_every_instance_only_when_every_bus_contribution_declares_it()
    {
        var model = MessagingCapabilityModel.Compose([
            MessagingProviderCapabilities.Transport("Broker", [MessageLane.Bus], true, supportsEveryInstance: true),
            MessagingProviderCapabilities.Transport("Broker", [MessageLane.Queue], true),
        ]);

        var act = () => model.EnsureEveryInstanceSupported("billing.prices");

        act.Should().NotThrow("a Queue-only contribution has no say over a Bus-lane property");
        model.Providers.Single(x => x.Role == MessagingProviderRole.Transport).SupportsEveryInstance.Should().BeTrue();
    }

    [Fact]
    public void should_name_the_provider_and_the_consumer_when_the_transport_has_no_every_instance_support()
    {
        var model = MessagingCapabilityModel.Compose([
            MessagingProviderCapabilities.Transport("Broker", [MessageLane.Bus, MessageLane.Queue], true),
        ]);

        var act = () => model.EnsureEveryInstanceSupported("billing.prices");

        act.Should().Throw<MessagingConfigurationException>().WithMessage("*'billing.prices'*'Broker'*");
    }

    [Fact]
    public void should_reject_an_every_instance_capability_without_the_bus_lane()
    {
        var act = () =>
            MessagingProviderCapabilities.Transport("Broker", [MessageLane.Queue], true, supportsEveryInstance: true);

        act.Should().Throw<ArgumentException>().WithMessage("*Bus lane*");
    }

    [Theory]
    [InlineData(EveryInstanceConflict.QueueLane, "*Queue lane*")]
    [InlineData(EveryInstanceConflict.ExplicitGroup, "*explicit group 'shared'*")]
    [InlineData(EveryInstanceConflict.InboxRetention, "*inbox retention*")]
    [InlineData(EveryInstanceConflict.CircuitBreaker, "*circuit breaker*")]
    public void should_fail_at_startup_when_an_every_instance_consumer_carries_a_durable_setting(
        EveryInstanceConflict conflict,
        string expectedMessage
    )
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
        });
        var lane = conflict is EveryInstanceConflict.QueueLane ? MessageLane.Queue : MessageLane.Bus;
        services.AddSingleton(
            MessageRegistration.ConsumerOnly(
                typeof(PriceChanged),
                lane,
                messageName: null,
                new MessageConsumerRegistration(
                    typeof(PriceCache),
                    lane,
                    IsAssemblyScan: false,
                    Group: conflict is EveryInstanceConflict.ExplicitGroup ? "shared" : null,
                    Concurrency: 1,
                    HandlerId: null,
                    PriceCache.Identity,
                    CircuitBreakerOverride: conflict is EveryInstanceConflict.CircuitBreaker
                        ? new ConsumerCircuitBreakerOptions()
                        : null,
                    ProviderConfigs: new Dictionary<Type, object>(),
                    InboxRetention: conflict is EveryInstanceConflict.InboxRetention ? TimeSpan.FromDays(1) : null
                )
                {
                    EveryInstance = true,
                },
                MessageOptions.InitialContractVersion
            )
        );
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetDrainedConsumerRegistry();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{PriceCache.Identity}'*")
            .WithMessage(expectedMessage);
    }

    [Fact]
    public void should_fail_at_startup_when_a_framework_every_instance_consumer_names_a_group()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFrameworkConsumerRegistration<PriceChanged, PriceCache>(
            MessageLane.Bus,
            PriceCache.Identity,
            MessageOptions.InitialContractVersion,
            group: "shared",
            everyInstance: true
        );
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetDrainedConsumerRegistry();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*explicit group 'shared'*");
    }

    [Fact]
    public void should_carry_every_instance_from_a_framework_registration_to_its_descriptor()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<EveryInstanceProbe>();
        services.AddFrameworkConsumerRegistration<PriceChanged, PriceCache>(
            MessageLane.Bus,
            PriceCache.Identity,
            MessageOptions.InitialContractVersion,
            everyInstance: true
        );
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var consumer = provider.GetDrainedConsumerRegistry().GetAll().Single();

        // then
        consumer.EveryInstance.Should().BeTrue();
    }

    public enum EveryInstanceConflict
    {
        QueueLane = 0,
        ExplicitGroup = 1,
        InboxRetention = 2,
        CircuitBreaker = 3,
    }
}
