// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Runtime;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests.Configuration;

public sealed class MessagingCapabilityModelTests : TestBase
{
    [Fact]
    public async Task should_reject_required_affinity_before_storage_processors_or_clients_start()
    {
        var sideEffects = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
            setup.Bus.ForMessage<SharedContract>(message => message.Contract("orders").RequireRoutingAffinity())
        );
        services.AddMessagingProviderCapabilities(_Transport("Unsupported", [MessageLane.Bus], true));
        services.AddMessagingProviderCapabilities(_Storage("InMemory"));
        services.AddSingleton<IStorageInitializer>(_ =>
        {
            sideEffects++;
            return new RecordingStorageInitializer(static () => { });
        });
        services.AddSingleton<IProcessingServer>(_ =>
        {
            sideEffects++;
            return new RecordingProcessingServer();
        });
        services.AddSingleton<IBusTransport>(_ =>
        {
            sideEffects++;
            return Substitute.For<IBusTransport>();
        });
        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        await act.Should().ThrowAsync<MessagingConfigurationException>().WithMessage("*affinity*unsupported*");
        sideEffects.Should().Be(0);
    }

    [Theory]
    [InlineData(DeliveryMode.Durable)]
    [InlineData(DeliveryMode.Direct)]
    public async Task should_reject_unknown_keyed_override_before_storage_or_transport_resolution(DeliveryMode mode)
    {
        var sideEffects = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
            setup.Bus.ForMessage<SharedContract>(message => message.Contract("orders"))
        );
        services.AddMessagingProviderCapabilities(
            MessagingProviderCapabilities.Transport(
                "Mapped",
                [MessageLane.Bus],
                true,
                [
                    new MessagingRoutingAffinityRoute(
                        MessageLane.Bus,
                        "orders",
                        new MessagingRoutingAffinityMapping("native-key")
                    ),
                ]
            )
        );
        services.AddMessagingProviderCapabilities(_Storage("InMemory"));
        services.AddSingleton<IDataStorage>(_ =>
        {
            sideEffects++;
            return Substitute.For<IDataStorage>();
        });
        services.AddSingleton<IBusTransport>(_ =>
        {
            sideEffects++;
            return Substitute.For<IBusTransport>();
        });
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBus>();

        var act = () =>
            bus.PublishAsync(
                new SharedContract(),
                new PublishOptions
                {
                    MessageName = "dynamic-unknown",
                    RoutingAffinityKey = "order-42",
                    DeliveryMode = mode,
                },
                AbortToken
            );

        await act.Should().ThrowAsync<MessagingConfigurationException>().WithMessage("*affinity*registered*verified*");
        sideEffects.Should().Be(0);
    }

    [Fact]
    public void should_compose_disjoint_bus_and_queue_contributions_from_same_provider()
    {
        var model = MessagingCapabilityModel.Compose([
            _Transport("Redis", [MessageLane.Bus], independentLaneTopology: true),
            _Transport("Redis", [MessageLane.Queue], independentLaneTopology: true),
            _Storage("InMemory"),
        ]);

        model.Supports(MessageLane.Bus, MessagingProviderRole.Transport).Should().BeTrue();
        model.Supports(MessageLane.Queue, MessagingProviderRole.Transport).Should().BeTrue();
        model.Providers.Should().ContainSingle(provider => provider.Provider == "Redis");
    }

    [Theory]
    [InlineData(false, "Redis", "Redis", "Bus")]
    [InlineData(true, "Redis", "Kafka", "transport provider")]
    public void should_reject_overlapping_or_different_transport_provider_contributions(
        bool differentProvider,
        string firstProvider,
        string secondProvider,
        string expectedMessage
    )
    {
        MessageLane[] secondLanes = differentProvider ? [MessageLane.Queue] : [MessageLane.Bus];

        var act = () =>
            MessagingCapabilityModel.Compose([
                _Transport(firstProvider, [MessageLane.Bus], independentLaneTopology: true),
                _Transport(secondProvider, secondLanes, independentLaneTopology: true),
                _Storage("InMemory"),
            ]);

        act.Should().Throw<MessagingConfigurationException>().WithMessage($"*{expectedMessage}*");
    }

    [Fact]
    public void should_reject_multiple_storage_contributions_deterministically()
    {
        var act = () =>
            MessagingCapabilityModel.Compose([
                _Transport("InMemory", [MessageLane.Bus, MessageLane.Queue], independentLaneTopology: true),
                _Storage("PostgreSql"),
                _Storage("SqlServer"),
            ]);

        act.Should().Throw<MessagingConfigurationException>().WithMessage("*exactly one storage provider*");
    }

    [Theory]
    [InlineData(MessagingInboxCapabilityTier.ProcessLocal, MessagingInboxCapabilityTier.DurableDedupeOnly)]
    [InlineData(MessagingInboxCapabilityTier.ProcessLocal, MessagingInboxCapabilityTier.Transactional)]
    [InlineData(MessagingInboxCapabilityTier.DurableDedupeOnly, MessagingInboxCapabilityTier.Transactional)]
    public void should_reject_storage_weaker_than_required_inbox_tier(
        MessagingInboxCapabilityTier available,
        MessagingInboxCapabilityTier required
    )
    {
        var model = MessagingCapabilityModel.Compose([
            _Transport("Transport", [MessageLane.Bus], independentLaneTopology: true),
            _Storage("TestStorage", available),
        ]);

        var act = () =>
            model.ValidateStartup(
                [new MessageRouteKey(typeof(SharedContract), "orders.changed", MessageLane.Bus)],
                hasDurableConsumers: true,
                requiredInboxCapability: required
            );

        act.Should()
            .Throw<MessagingConfigurationException>()
            .WithMessage($"*{required}*TestStorage*{available}*explicitly*");
        model.InboxCapability.Should().Be(available);
    }

    [Theory]
    [InlineData(MessagingInboxCapabilityTier.ProcessLocal, MessagingInboxCapabilityTier.ProcessLocal)]
    [InlineData(MessagingInboxCapabilityTier.DurableDedupeOnly, MessagingInboxCapabilityTier.ProcessLocal)]
    [InlineData(MessagingInboxCapabilityTier.Transactional, MessagingInboxCapabilityTier.ProcessLocal)]
    [InlineData(MessagingInboxCapabilityTier.DurableDedupeOnly, MessagingInboxCapabilityTier.DurableDedupeOnly)]
    [InlineData(MessagingInboxCapabilityTier.Transactional, MessagingInboxCapabilityTier.DurableDedupeOnly)]
    [InlineData(MessagingInboxCapabilityTier.Transactional, MessagingInboxCapabilityTier.Transactional)]
    public void should_accept_storage_at_or_above_required_inbox_tier_and_preserve_declared_capability(
        MessagingInboxCapabilityTier available,
        MessagingInboxCapabilityTier required
    )
    {
        var model = MessagingCapabilityModel.Compose([
            _Transport("Transport", [MessageLane.Bus], independentLaneTopology: true),
            _Storage("TestStorage", available),
        ]);

        model.ValidateStartup(
            [new MessageRouteKey(typeof(SharedContract), "orders.changed", MessageLane.Bus)],
            hasDurableConsumers: true,
            requiredInboxCapability: required
        );

        model.InboxCapability.Should().Be(available);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(-1, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(int.MaxValue, true)]
    public void should_reject_undefined_required_inbox_tier(int required, bool hasDurableConsumers)
    {
        var model = MessagingCapabilityModel.Compose([
            _Transport("Transport", [MessageLane.Bus], independentLaneTopology: true),
            _Storage("TestStorage", MessagingInboxCapabilityTier.Transactional),
        ]);

        var act = () =>
            model.ValidateStartup(
                [new MessageRouteKey(typeof(SharedContract), "orders.changed", MessageLane.Bus)],
                hasDurableConsumers: hasDurableConsumers,
                requiredInboxCapability: (MessagingInboxCapabilityTier)required
            );

        act.Should().Throw<ArgumentException>().WithMessage("*requiredInboxCapability*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void should_reject_undefined_declared_inbox_tier(int available)
    {
        var act = () => _Storage("TestStorage", (MessagingInboxCapabilityTier)available);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task should_reject_missing_durable_identity_before_storage_or_processors_start()
    {
        var storageInitializerCalls = 0;
        var processingServerFactoryCalls = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
            setup.Bus.ForMessage<SharedContract>(message => message.Consumer<SharedConsumer>(static _ => { }))
        );
        services.AddMessagingProviderCapabilities(
            _Transport("Transport", [MessageLane.Bus], independentLaneTopology: true)
        );
        services.AddMessagingProviderCapabilities(
            _Storage("Transactional", MessagingInboxCapabilityTier.Transactional)
        );
        services.AddSingleton<IStorageInitializer>(_ =>
        {
            Interlocked.Increment(ref storageInitializerCalls);
            return new RecordingStorageInitializer(static () => { });
        });
        services.AddSingleton<IProcessingServer>(_ =>
        {
            Interlocked.Increment(ref processingServerFactoryCalls);
            return new RecordingProcessingServer();
        });

        await using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        await act.Should().ThrowAsync<MessagingConfigurationException>().WithMessage("*stable consumer identity*");
        storageInitializerCalls.Should().Be(0);
        processingServerFactoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_start_with_explicit_durable_dedupe_only_opt_down_and_expose_tier()
    {
        var storageInitializeCalls = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
            setup.Bus.ForMessage<SharedContract>(message =>
                message.Consumer<SharedConsumer>(consumer => consumer.ConsumerIdentity("orders-projection"))
            );
        });
        services.AddMessagingProviderCapabilities(
            _Transport("Transport", [MessageLane.Bus], independentLaneTopology: true)
        );
        services.AddMessagingProviderCapabilities(
            _Storage("PostgreSql", MessagingInboxCapabilityTier.DurableDedupeOnly)
        );
        services.RemoveAll<IProcessingServer>();
        services.AddSingleton<IStorageInitializer>(
            new RecordingStorageInitializer(() => Interlocked.Increment(ref storageInitializeCalls))
        );

        await using var provider = services.BuildServiceProvider();
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        bootstrapper.IsStarted.Should().BeTrue();
        storageInitializeCalls.Should().Be(1);
        provider
            .GetRequiredService<IMessagingCapabilityModel>()
            .InboxCapability.Should()
            .Be(MessagingInboxCapabilityTier.DurableDedupeOnly);
    }

    [Theory]
    [InlineData("NATS")]
    [InlineData("Apache Pulsar")]
    [InlineData("RabbitMQ")]
    public void should_reject_same_name_dual_lane_route_when_provider_has_no_independent_topology(string provider)
    {
        var model = MessagingCapabilityModel.Compose([
            _Transport(provider, [MessageLane.Bus, MessageLane.Queue], independentLaneTopology: false),
            _Storage("InMemory"),
        ]);

        var act = () =>
            model.ValidateStartup([
                new MessageRouteKey(typeof(SharedContract), "orders.changed", MessageLane.Bus),
                new MessageRouteKey(typeof(SharedContract), "Orders.Changed", MessageLane.Queue),
            ]);

        act.Should().Throw<MessagingConfigurationException>().WithMessage($"*{provider}*independent*lane*topology*");
    }

    [Theory]
    [InlineData("NATS")]
    [InlineData("Apache Pulsar")]
    [InlineData("RabbitMQ")]
    public void should_reject_cross_contract_name_collision_when_provider_has_no_independent_topology(string provider)
    {
        var model = MessagingCapabilityModel.Compose([
            _Transport(provider, [MessageLane.Bus, MessageLane.Queue], independentLaneTopology: false),
            _Storage("InMemory"),
        ]);

        var act = () =>
            model.ValidateStartup([
                new MessageRouteKey(typeof(SharedContract), "orders.changed", MessageLane.Bus),
                new MessageRouteKey(typeof(OtherContract), "Orders.Changed", MessageLane.Queue),
            ]);

        act.Should().Throw<MessagingConfigurationException>().WithMessage($"*{provider}*independent*lane*topology*");
    }

    [Fact]
    public void should_make_custom_inert_contribution_added_after_messaging_visible_to_the_model()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(_ => { });

        services.AddMessagingProviderCapabilities(
            _Transport("Custom", [MessageLane.Bus], independentLaneTopology: true)
        );

        using var provider = services.BuildServiceProvider();
        var model = provider.GetRequiredService<IMessagingCapabilityModel>();

        model.DeclaredCapabilities.Should().ContainSingle(capability => capability.Provider == "Custom");
    }

    [Fact]
    public async Task should_reject_publisher_only_dual_lane_routes_before_startup_side_effects()
    {
        var storageInitializerCalls = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.Bus.ForMessage<SharedContract>(message => message.Contract("orders.changed"));
            setup.Queue.ForMessage<SharedContract>(message => message.Contract("orders.changed"));
        });
        services.AddMessagingProviderCapabilities(
            _Transport("SharedTopology", [MessageLane.Bus, MessageLane.Queue], independentLaneTopology: false)
        );
        services.AddMessagingProviderCapabilities(_Storage("InMemory"));
        services.AddSingleton<IStorageInitializer>(_ =>
        {
            Interlocked.Increment(ref storageInitializerCalls);
            return Substitute.For<IStorageInitializer>();
        });

        await using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage("*SharedTopology*independent*lane*topology*");
        storageInitializerCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_validate_only_effective_lane_mapping_when_lane_override_replaces_global_fallback()
    {
        var storageInitializeCalls = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.WithMessageNameMapping<SharedContract>("orders.global");
            setup.Bus.ForMessage<SharedContract>(message => message.Contract("orders.bus"));
        });
        services.AddMessagingProviderCapabilities(
            _Transport("SharedTopology", [MessageLane.Bus, MessageLane.Queue], independentLaneTopology: false)
        );
        services.AddMessagingProviderCapabilities(_Storage("InMemory"));
        services.RemoveAll<IProcessingServer>();
        services.AddSingleton<IStorageInitializer>(
            new RecordingStorageInitializer(() => Interlocked.Increment(ref storageInitializeCalls))
        );

        await using var provider = services.BuildServiceProvider();
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        bootstrapper.IsStarted.Should().BeTrue();
        storageInitializeCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_check_message_name_collisions_using_effective_lane_mapping()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.WithMessageNameMapping<SharedContract>("orders.global");
            setup.Bus.ForMessage<SharedContract>(message => message.Contract("orders.bus"));
            setup.Bus.ForMessage<OtherContract>(message => message.Contract("orders.global"));
        });
        services.AddMessagingProviderCapabilities(
            _Transport("IndependentTopology", [MessageLane.Bus, MessageLane.Queue], independentLaneTopology: true)
        );
        services.AddMessagingProviderCapabilities(_Storage("InMemory"));
        services.RemoveAll<IProcessingServer>();
        services.AddSingleton<IStorageInitializer>(new RecordingStorageInitializer(static () => { }));

        await using var provider = services.BuildServiceProvider();
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        bootstrapper.IsStarted.Should().BeTrue();
    }

    [Fact]
    public async Task should_reject_raw_transport_before_middleware_or_transport_side_effects()
    {
        var recorder = new PublishSideEffectRecorder();
        var transport = Substitute.For<IBusTransport>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(recorder);
        var builder = services.AddHeadlessMessaging(_ => { });
        builder.AddBusPublishMiddleware<RecordingPublishMiddleware>();
        services.AddSingleton(transport);

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IBus));

        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBus>();
        var act = () =>
            bus.PublishAsync(
                new SharedContract(),
                new PublishOptions { DeliveryMode = DeliveryMode.Direct },
                cancellationToken: AbortToken
            );

        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage("*capabilit*AddMessagingProviderCapabilities*");
        recorder.MiddlewareCalls.Should().Be(0);
        _ = transport.DidNotReceiveWithAnyArgs().SendAsync(default!, AbortToken);
    }

    [Fact]
    public async Task should_reject_unsupported_queue_before_middleware_or_transport_side_effects()
    {
        var recorder = new PublishSideEffectRecorder();
        var queueTransport = Substitute.For<IQueueTransport>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(recorder);
        var builder = services.AddHeadlessMessaging(_ => { });
        builder.AddPublishMiddlewareFor<RecordingTypedPublishMiddleware, SharedContract>(MessageLane.Bus);
        services.AddMessagingProviderCapabilities(
            _Transport("BusOnly", [MessageLane.Bus], independentLaneTopology: true)
        );
        services.AddSingleton(queueTransport);

        await using var provider = services.BuildServiceProvider();
        var queue = provider.GetRequiredService<IQueue>();
        var act = () =>
            queue.EnqueueAsync(
                new SharedContract(),
                new QueueOptions { DeliveryMode = DeliveryMode.Direct },
                cancellationToken: AbortToken
            );

        await act.Should().ThrowAsync<MessagingConfigurationException>().WithMessage("*Queue*unsupported*");
        recorder.MiddlewareCalls.Should().Be(0);
        _ = queueTransport.DidNotReceiveWithAnyArgs().SendAsync(default!, AbortToken);
    }

    [Fact]
    public async Task should_reject_unsupported_outbox_before_resolving_storage_or_running_middleware()
    {
        var storageFactoryCalls = 0;
        var recorder = new PublishSideEffectRecorder();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(recorder);
        var builder = services.AddHeadlessMessaging(_ => { });
        builder.AddBusPublishMiddleware<RecordingPublishMiddleware>();
        services.AddMessagingProviderCapabilities(
            _Transport("BusOnly", [MessageLane.Bus], independentLaneTopology: true)
        );
        services.AddSingleton<IDataStorage>(_ =>
        {
            Interlocked.Increment(ref storageFactoryCalls);
            return Substitute.For<IDataStorage>();
        });

        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBus>();
        var act = () =>
            bus.PublishAsync(
                new SharedContract(),
                new PublishOptions { DeliveryMode = DeliveryMode.Durable },
                AbortToken
            );

        await act.Should().ThrowAsync<MessagingConfigurationException>().WithMessage("*storage*capabilit*");
        storageFactoryCalls.Should().Be(0);
        recorder.MiddlewareCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_reject_delayed_outbox_before_resolving_storage_or_running_middleware(MessageLane lane)
    {
        var storageFactoryCalls = 0;
        var recorder = new PublishSideEffectRecorder();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(recorder);
        var builder = services.AddHeadlessMessaging(_ => { });
        builder.AddPublishMiddlewareFor<RecordingTypedPublishMiddleware, SharedContract>(lane);
        services.AddMessagingProviderCapabilities(_Transport("Transport", [lane], independentLaneTopology: true));
        services.AddMessagingProviderCapabilities(
            MessagingProviderCapabilities.Storage(
                "NoDelayStorage",
                [lane],
                supportsDelayedScheduling: false,
                inboxCapability: MessagingInboxCapabilityTier.Transactional
            )
        );
        services.AddSingleton<IDataStorage>(_ =>
        {
            Interlocked.Increment(ref storageFactoryCalls);
            return Substitute.For<IDataStorage>();
        });

        await using var provider = services.BuildServiceProvider();
        Func<Task<PublishReceipt>> act = lane switch
        {
            MessageLane.Bus => () =>
                provider
                    .GetRequiredService<IBus>()
                    .PublishAsync(
                        new SharedContract(),
                        new PublishOptions { Delay = TimeSpan.FromSeconds(1), DeliveryMode = DeliveryMode.Durable },
                        AbortToken
                    ),
            MessageLane.Queue => () =>
                provider
                    .GetRequiredService<IQueue>()
                    .EnqueueAsync(
                        new SharedContract(),
                        new QueueOptions { Delay = TimeSpan.FromSeconds(1), DeliveryMode = DeliveryMode.Durable },
                        AbortToken
                    ),
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, "Unknown messaging lane."),
        };

        await act.Should().ThrowAsync<MessagingConfigurationException>().WithMessage("*does not support delayed*");
        storageFactoryCalls.Should().Be(0);
        recorder.MiddlewareCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_validate_startup_before_resolving_storage_or_processing_servers()
    {
        var storageFactoryCalls = 0;
        var storageInitializeCalls = 0;
        var processingServerFactoryCalls = 0;
        var clientFactoryCalls = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(_ => { });
        services.AddMessagingProviderCapabilities(_Storage("TestStorage"));
        services.AddSingleton<IStorageInitializer>(_ =>
        {
            Interlocked.Increment(ref storageFactoryCalls);
            return new RecordingStorageInitializer(() => Interlocked.Increment(ref storageInitializeCalls));
        });
        services.AddSingleton<IProcessingServer>(_ =>
        {
            Interlocked.Increment(ref processingServerFactoryCalls);
            return new RecordingProcessingServer();
        });
        services.AddSingleton<IConsumerClientFactory>(_ =>
        {
            Interlocked.Increment(ref clientFactoryCalls);
            return Substitute.For<IConsumerClientFactory>();
        });

        await using var provider = services.BuildServiceProvider();
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();
        var act = () => bootstrapper.BootstrapAsync(AbortToken);

        await act.Should().ThrowAsync<MessagingConfigurationException>().WithMessage("*transport provider*");
        storageFactoryCalls.Should().Be(0);
        storageInitializeCalls.Should().Be(0);
        processingServerFactoryCalls.Should().Be(0);
        clientFactoryCalls.Should().Be(0);
        bootstrapper.IsStarted.Should().BeFalse();
    }

    [Fact]
    public void should_accept_supported_composition_and_freeze_declared_capabilities()
    {
        var model = MessagingCapabilityModel.Compose([
            _Transport("InMemory", [MessageLane.Bus, MessageLane.Queue], independentLaneTopology: true),
            _Storage("InMemory"),
        ]);

        model.ValidateStartup([
            new MessageRouteKey(typeof(SharedContract), "orders.changed", MessageLane.Bus),
            new MessageRouteKey(typeof(SharedContract), "orders.changed", MessageLane.Queue),
        ]);

        model.Supports(MessageLane.Bus, MessagingProviderRole.Transport).Should().BeTrue();
        model.Supports(MessageLane.Queue, MessagingProviderRole.Storage).Should().BeTrue();
        model.IsFrozen.Should().BeTrue();
    }

    private static MessagingProviderCapabilities _Transport(
        string provider,
        IReadOnlyCollection<MessageLane> lanes,
        bool independentLaneTopology
    )
    {
        return MessagingProviderCapabilities.Transport(provider, lanes, independentLaneTopology);
    }

    private static MessagingProviderCapabilities _Storage(
        string provider,
        MessagingInboxCapabilityTier inboxCapability = MessagingInboxCapabilityTier.Transactional
    )
    {
        return MessagingProviderCapabilities.Storage(
            provider,
            [MessageLane.Bus, MessageLane.Queue],
            supportsDelayedScheduling: true,
            inboxCapability: inboxCapability
        );
    }

    private sealed record SharedContract;

    private sealed record OtherContract;

    private sealed class SharedConsumer : IConsume<SharedContract>
    {
        public ValueTask ConsumeAsync(ConsumeContext<SharedContract> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PublishSideEffectRecorder
    {
        public int MiddlewareCalls;
    }

    private sealed class RecordingPublishMiddleware(PublishSideEffectRecorder recorder)
        : IPublishMiddleware<PublishContext>
    {
        public ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
        {
            Interlocked.Increment(ref recorder.MiddlewareCalls);
            return next();
        }
    }

    private sealed class RecordingTypedPublishMiddleware(PublishSideEffectRecorder recorder)
        : IPublishMiddleware<PublishContext<SharedContract>>
    {
        public ValueTask InvokeAsync(PublishContext<SharedContract> context, Func<ValueTask> next)
        {
            Interlocked.Increment(ref recorder.MiddlewareCalls);
            return next();
        }
    }

    private sealed class RecordingStorageInitializer(Action initialize) : IStorageInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            initialize();
            return Task.CompletedTask;
        }

        public string GetPublishedTableName() => "published";

        public string GetReceivedTableName() => "received";
    }

    private sealed class RecordingProcessingServer : IProcessingServer
    {
        public ValueTask StartAsync(CancellationToken stoppingToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
