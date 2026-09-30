// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Kafka;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Theory]
    [InlineData("random", false)]
    [InlineData("unknown", false)]
    [InlineData("consistent_random", true)]
    [InlineData("murmur2", true)]
    public async Task should_snapshot_affinity_support_without_resolving_clients(string partitioner, bool supported)
    {
        var effects = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseKafka(options =>
            {
                options.Servers = "localhost:9092";
                options.MainConfig["partitioner"] = partitioner;
            });
        });
        services.ConfigureMessaging(messaging =>
            messaging.Message<KafkaBusContract>("orders").OnQueue(queue => queue.RequireRoutingAffinity())
        );
        services.AddSingleton<IKafkaConnectionPool>(_ =>
        {
            effects++;
            return Substitute.For<IKafkaConnectionPool>();
        });
        services.AddSingleton<IQueueTransport>(_ =>
        {
            effects++;
            return Substitute.For<IQueueTransport>();
        });
        await using var provider = services.BuildServiceProvider();

        var model = provider.GetRequiredService<IMessagingCapabilityModel>();
        model.Providers.Single().RoutingAffinityRoutes.Any().Should().Be(supported);
        if (!supported)
        {
            var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
            await act.Should().ThrowAsync<MessagingConfigurationException>().WithMessage("*affinity*unsupported*");
        }

        effects.Should().Be(0);
    }

    [Fact]
    public async Task should_reject_bus_route_before_storage_or_broker_side_effects()
    {
        var storageInitializeCalls = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(options =>
        {
            options.UseKafka("localhost:9092");
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<KafkaBusContract>("orders.changed");
            messaging.AddModule<KafkaBusConsumerModule>();
        });
        services.AddMessagingProviderCapabilities(
            MessagingProviderCapabilities.Storage(
                "TestStorage",
                [MessageLane.Bus, MessageLane.Queue],
                supportsDelayedScheduling: true,
                inboxCapability: MessagingInboxCapabilityTier.Transactional
            )
        );
        services.AddSingleton<IStorageInitializer>(new RecordingStorageInitializer(() => storageInitializeCalls++));

        await using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage("*Kafka*does not support Bus*Supported lanes: Queue*");
        storageInitializeCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_accept_message_contract_at_startup_when_transport_carries_only_the_queue()
    {
        // A contract names the message on both lanes, so startup must skip the Bus lane Kafka does not carry. The
        // storage initializer runs right after that validation and stops the bootstrap before any processor starts.
        var storageInitializeCalls = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(options => options.UseKafka("localhost:9092"));
        services.ConfigureMessaging(messaging => messaging.Message<KafkaBusContract>("orders.changed", "v1"));
        services.AddMessagingProviderCapabilities(
            MessagingProviderCapabilities.Storage(
                "TestStorage",
                [MessageLane.Bus, MessageLane.Queue],
                supportsDelayedScheduling: true,
                inboxCapability: MessagingInboxCapabilityTier.Transactional
            )
        );
        // Bootstrap resolves the processing servers, and so the storage they write to, before it initializes storage.
        services.AddSingleton(Substitute.For<IDataStorage>());
        services.AddSingleton<IStorageInitializer>(
            new RecordingStorageInitializer(() =>
            {
                storageInitializeCalls++;
                throw new StorageInitializationReachedException();
            })
        );

        await using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        await act.Should().ThrowAsync<StorageInitializationReachedException>();
        storageInitializeCalls.Should().Be(1);
    }

    [Fact]
    public void should_register_services_when_use_kafka_with_bootstrap_servers()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(options => options.UseKafka("localhost:9092"));

        var provider = services.BuildServiceProvider();

        // then
        provider.GetService<IQueueTransport>().Should().BeOfType<KafkaTransport>();
        provider.GetService<IConsumerClientFactory>().Should().NotBeNull();
        provider.GetService<IConsumerClientFactory>().Should().BeOfType<KafkaConsumerClientFactory>();
        provider.GetService<IKafkaConnectionPool>().Should().NotBeNull();
        provider.GetService<IKafkaConnectionPool>().Should().BeOfType<KafkaConnectionPool>();
    }

    [Fact]
    public void should_configure_options_when_use_kafka_with_configure_action()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(options =>
        {
            options.UseKafka(opt =>
            {
                opt.Servers = "broker1:9092,broker2:9092";
                opt.ConnectionPoolSize = 20;
            });
        });

        var provider = services.BuildServiceProvider();
        var kafkaOptions = provider.GetRequiredService<IOptions<KafkaMessagingOptions>>().Value;

        // then
        kafkaOptions.Servers.Should().Be("broker1:9092,broker2:9092");
        kafkaOptions.ConnectionPoolSize.Should().Be(20);
    }

    [Fact]
    public void should_throw_when_use_kafka_configure_is_null()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessMessaging(options => options.UseKafka((Action<KafkaMessagingOptions>)null!));

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_register_message_queue_marker_service_when_use_kafka()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(options => options.UseKafka("localhost:9092"));

        var provider = services.BuildServiceProvider();
        var marker = provider.GetService<MessageQueueMarkerService>();

        // then
        marker.Should().NotBeNull();
        marker!.Name.Should().Be("Kafka");
    }

    [Fact]
    public void should_register_as_singletons_when_use_kafka()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(options => options.UseKafka("localhost:9092"));

        var provider = services.BuildServiceProvider();

        // when
        var pool1 = provider.GetService<IKafkaConnectionPool>();
        var pool2 = provider.GetService<IKafkaConnectionPool>();

        // then
        pool1.Should().BeSameAs(pool2);
    }

    private sealed record KafkaBusContract;

    // A Bus consumer is what puts a message on the Bus lane, which Kafka does not have.
    private sealed class KafkaBusConsumer : IConsume<KafkaBusContract>
    {
        public ValueTask ConsumeAsync(ConsumeContext<KafkaBusContract> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class KafkaBusConsumerModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddBusConsumer<KafkaBusConsumer, KafkaBusContract>(
                "tests.kafka.bus-consumer",
                everyInstance: false,
                policy: null,
                static (_, _, _) => ValueTask.CompletedTask
            );
    }

    private sealed class StorageInitializationReachedException : Exception;

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
}
