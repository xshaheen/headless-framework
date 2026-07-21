// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Runtime;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Configuration;

public sealed class MessagingCapabilityModelTests : TestBase
{
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
            setup.Bus.ForMessage<SharedContract>(message => message.MessageName("orders.changed"));
            setup.Queue.ForMessage<SharedContract>(message => message.MessageName("orders.changed"));
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
        var act = () => bus.PublishAsync(new SharedContract(), cancellationToken: AbortToken);

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
        var act = () => queue.EnqueueAsync(new SharedContract(), cancellationToken: AbortToken);

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
        var outbox = provider.GetRequiredService<IOutboxBus>();
        var act = () => outbox.PublishAsync(new SharedContract(), cancellationToken: AbortToken);

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
            MessagingProviderCapabilities.Storage("NoDelayStorage", [lane], supportsDelayedScheduling: false)
        );
        services.AddSingleton<IDataStorage>(_ =>
        {
            Interlocked.Increment(ref storageFactoryCalls);
            return Substitute.For<IDataStorage>();
        });

        await using var provider = services.BuildServiceProvider();
        Func<Task> act = lane switch
        {
            MessageLane.Bus => () =>
                provider
                    .GetRequiredService<IOutboxBus>()
                    .PublishAsync(
                        new SharedContract(),
                        new PublishOptions { Delay = TimeSpan.FromSeconds(1) },
                        AbortToken
                    ),
            MessageLane.Queue => () =>
                provider
                    .GetRequiredService<IOutboxQueue>()
                    .EnqueueAsync(
                        new SharedContract(),
                        new EnqueueOptions { Delay = TimeSpan.FromSeconds(1) },
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

    private static MessagingProviderCapabilities _Storage(string provider)
    {
        return MessagingProviderCapabilities.Storage(
            provider,
            [MessageLane.Bus, MessageLane.Queue],
            supportsDelayedScheduling: true
        );
    }

    private sealed record SharedContract;

    private sealed record OtherContract;

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
