// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Messaging.Testing;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class TransportReplacementTests : TestBase
{
    // Nothing listens on port 1, so a host that still sent through the broker transport would fail to connect.
    private const string _UnreachableHost = "127.0.0.1";
    private const int _UnreachablePort = 1;

    private const string _InMemoryBrokerName = "in_memory";

    [Fact]
    public async Task should_deliver_in_memory_when_rabbitmq_transport_is_replaced()
    {
        // given
        var builder = _CreateHostBuilder();
        builder.Services.AddHeadlessMessaging(setup =>
        {
            _UseUnreachableRabbitMq(setup);
            _UseInMemoryStorage(setup);
        });
        builder.Services.ConfigureMessaging(messaging =>
        {
            messaging.Message<OrderCreatedEvent>("order-created");
            messaging.AddModule<OrderCreatedModule>();
        });
        builder.Services.ReplaceMessagingTransportWithInMemory().AddMessagingTestHarness();
        using var host = builder.Build();

        // when
        await _StartAsync(host);
        var harness = host.Services.GetRequiredService<MessagingTestHarness>();
        await harness.Publisher.PublishAsync(new OrderCreatedEvent("SWAP-1", 10m), cancellationToken: AbortToken);
        var consumed = await harness.WaitForConsumedAsync<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then
        consumed.Message.Should().BeOfType<OrderCreatedEvent>().Which.OrderId.Should().Be("SWAP-1");
        harness.GetRequiredService<IBusTransport>().BrokerAddress.Name.Should().Be(_InMemoryBrokerName);
        harness.GetRequiredService<IQueueTransport>().BrokerAddress.Name.Should().Be(_InMemoryBrokerName);
        host.Services.GetServices<IConsumerClientFactory>()
            .Should()
            .ContainSingle()
            .Which.GetType()
            .Assembly.GetName()
            .Name.Should()
            .Be("Headless.Messaging.InMemory");

        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_fail_startup_on_invalid_nats_options_when_transport_is_not_replaced()
    {
        // given — the same invalid options the replacement test starts with, proving they do fail validation
        var builder = _CreateHostBuilder();
        builder.Services.AddHeadlessMessaging(setup =>
        {
            _UseInvalidNats(setup);
            _UseInMemoryStorage(setup);
        });
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public async Task should_remove_nats_options_validation_processing_server_and_health_check_when_transport_is_replaced()
    {
        // given
        var builder = _CreateHostBuilder();
        builder.Services.AddHeadlessMessaging(setup =>
        {
            _UseInvalidNats(setup);
            _UseInMemoryStorage(setup);
        });
        builder.Services.ReplaceMessagingTransportWithInMemory();
        using var host = builder.Build();

        // when — startup validates options first, so surviving NATS validation would throw here
        await _StartAsync(host);

        // then
        var nats = typeof(NatsMessagingOptions).Assembly;
        builder.Services.Should().NotContain(descriptor => _Names(descriptor, nats));
        host.Services.GetServices<IProcessingServer>().Should().NotContain(server => server.GetType().Assembly == nats);
        host.Services.GetServices<IValidateOptions<NatsMessagingOptions>>().Should().BeEmpty();

        var checks = host
            .Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(static registration => registration.Name)
            .ToArray();
        checks.Should().NotContain("messaging-nats");
        checks
            .Should()
            .Contain("messaging", "the shared health check infrastructure the NATS extension added first stays");

        await host.StopAsync(AbortToken);
    }

    [Fact]
    public void should_reject_harness_naming_transport_and_replacement_when_broker_transport_is_registered()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            _UseUnreachableRabbitMq(setup);
            _UseInMemoryStorage(setup);
        });

        // when
        var act = () => services.AddMessagingTestHarness();

        // then — a rejected collection is not marked configured, so a second call guards again
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'RabbitMQ'*ReplaceMessagingTransportWithInMemory()*");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task should_fail_startup_naming_route_and_replaced_transport_when_message_requires_routing_affinity()
    {
        // given
        var builder = _CreateHostBuilder();
        builder.Services.AddHeadlessMessaging(setup =>
        {
            _UseUnreachableRabbitMq(setup);
            _UseInMemoryStorage(setup);
        });
        builder.Services.ConfigureMessaging(messaging =>
            messaging
                .Message<OrderCreatedEvent>("affinity-order")
                .OnQueue(static queue => queue.RequireRoutingAffinity())
        );
        builder.Services.ReplaceMessagingTransportWithInMemory().AddMessagingTestHarness();
        using var host = builder.Build();
        await host.StartAsync(AbortToken);
        var bootstrapper = host.Services.GetRequiredService<IBootstrapper>();

        // when — joins the background bootstrap, which is where startup validation of the routes runs
        var act = () => bootstrapper.BootstrapAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage("*'affinity-order' (Queue)*ReplaceMessagingTransportWithInMemory()*'RabbitMQ'*");
    }

    [Fact]
    public async Task should_install_in_memory_transport_when_host_registers_no_transport()
    {
        // given — composes with the harness's own host as well as with an application host
        await using var harness = await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddHeadlessMessaging(_UseInMemoryStorage);
                services.ConfigureMessaging(messaging =>
                {
                    messaging.Message<OrderCreatedEvent>("order-created");
                    messaging.AddModule<OrderCreatedModule>();
                });
                services.ReplaceMessagingTransportWithInMemory();
            },
            AbortToken
        );

        // when
        await harness.Publisher.PublishAsync(new OrderCreatedEvent("NO-BROKER", 1m), cancellationToken: AbortToken);
        var consumed = await harness.WaitForConsumedAsync<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then
        consumed.Message.Should().BeOfType<OrderCreatedEvent>().Which.OrderId.Should().Be("NO-BROKER");
    }

    [Fact]
    public async Task should_fail_startup_naming_no_broker_when_message_requires_routing_affinity_and_host_had_no_transport()
    {
        // given
        var builder = _CreateHostBuilder();
        builder.Services.AddHeadlessMessaging(_UseInMemoryStorage);
        builder.Services.ConfigureMessaging(messaging =>
            messaging.Message<OrderCreatedEvent>("affinity-order").OnBus(static bus => bus.RequireRoutingAffinity())
        );
        builder.Services.ReplaceMessagingTransportWithInMemory();
        using var host = builder.Build();
        await host.StartAsync(AbortToken);
        var bootstrapper = host.Services.GetRequiredService<IBootstrapper>();

        // when — joins the background bootstrap, which is where startup validation of the routes runs
        var act = () => bootstrapper.BootstrapAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage("*'affinity-order' (Bus)*in place of no broker transport*");
    }

    [Fact]
    public async Task should_replace_custom_transport_registered_through_an_extension()
    {
        // given — a transport the replacement knows nothing about, recognized only by what its extension registered
        await using var harness = await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddHeadlessMessaging(setup =>
                {
                    _UseInMemoryStorage(setup);
                    setup.RegisterExtension(new QueueOnlyTransportExtension());
                });
                services.ReplaceMessagingTransportWithInMemory();
            },
            AbortToken
        );

        // when
        await harness.Queue.EnqueueAsync(
            new OrderCreatedEvent("queue-only", 10m),
            new QueueOptions { MessageName = "queue-only-order-created" },
            AbortToken
        );
        var published = await harness.WaitForPublishedAsync<OrderCreatedEvent>(
            MessageLane.Queue,
            TimeSpan.FromSeconds(5),
            AbortToken
        );

        // then
        published.Lane.Should().Be(MessageLane.Queue);
        harness.GetRequiredService<IQueueTransport>().BrokerAddress.Name.Should().Be(_InMemoryBrokerName);
        harness
            .ServiceProvider.GetServices<IConsumerClientFactory>()
            .Should()
            .NotContain(factory => factory is UnusedConsumerClientFactory);
    }

    [Fact]
    public void should_leave_collection_unchanged_when_transport_is_already_in_memory()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            _UseInMemoryStorage(setup);
        });
        ServiceDescriptor[] before = [.. services];

        // when
        services.ReplaceMessagingTransportWithInMemory();

        // then
        services.Should().Equal(before);
    }

    [Fact]
    public void should_replace_transport_registered_by_a_later_add_headless_messaging_call()
    {
        // given — the transport arrives in a second AddHeadlessMessaging call, as a module's own setup may add it
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(_UseInMemoryStorage);
        services.AddHeadlessMessaging(_UseUnreachableRabbitMq);

        // when
        services.ReplaceMessagingTransportWithInMemory();

        // then
        var rabbitMq = typeof(Headless.Messaging.RabbitMq.RabbitMqMessagingOptions).Assembly;
        services.Should().NotContain(descriptor => _Names(descriptor, rabbitMq));
        var act = () => services.AddMessagingTestHarness();
        act.Should().NotThrow();
    }

    [Fact]
    public void should_leave_collection_unchanged_when_replacement_runs_twice()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(setup =>
        {
            _UseUnreachableRabbitMq(setup);
            _UseInMemoryStorage(setup);
        });
        services.ReplaceMessagingTransportWithInMemory();
        ServiceDescriptor[] afterFirst = [.. services];

        // when
        services.ReplaceMessagingTransportWithInMemory();

        // then
        services.Should().Equal(afterFirst);
    }

    [Fact]
    public void should_reject_replacement_when_messaging_is_not_registered()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.ReplaceMessagingTransportWithInMemory();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*after AddHeadlessMessaging(...)*");
    }

    [Fact]
    public void should_reject_replacement_when_harness_is_already_registered()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            _UseInMemoryStorage(setup);
        });
        services.AddMessagingTestHarness();

        // when
        var act = () => services.ReplaceMessagingTransportWithInMemory();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*before AddMessagingTestHarness()*");
    }

    [Fact]
    public void should_reject_replacement_when_transport_is_registered_outside_provider_setup()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(_UseInMemoryStorage);
        services.AddSingleton(new MessageQueueMarkerService("Hand-registered"));

        // when
        var act = () => services.ReplaceMessagingTransportWithInMemory();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*'Hand-registered'*");
    }

    [Fact]
    public void should_reject_null_services()
    {
        // when
        var act = () => MessagingTestHarnessExtensions.ReplaceMessagingTransportWithInMemory(null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    private static HostApplicationBuilder _CreateHostBuilder()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddLogging();

        return builder;
    }

    /// <summary>
    /// Starts the host, then joins the messaging bootstrap. The bootstrapper is a background service, so the host's own
    /// start returns before bootstrap finishes and never surfaces its failure.
    /// </summary>
    private static async Task _StartAsync(IHost host)
    {
        await host.StartAsync(AbortToken);
        await host.Services.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
    }

    private static void _UseInMemoryStorage(MessagingSetupBuilder setup)
    {
        setup.UseInMemoryStorage();
        setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
    }

    private static void _UseUnreachableRabbitMq(MessagingSetupBuilder setup)
    {
        setup.UseRabbitMq(options =>
        {
            options.HostName = _UnreachableHost;
            options.Port = _UnreachablePort;
            options.UserName = "headless-tests";
            options.Password = "not-a-real-secret";
        });
    }

    private static void _UseInvalidNats(MessagingSetupBuilder setup)
    {
        // An empty server list fails NATS options validation, which runs when the host starts.
        setup.UseNats(static options => options.Servers = string.Empty);
    }

    /// <summary>Whether <paramref name="descriptor"/> names a type from <paramref name="assembly"/> anywhere.</summary>
    private static bool _Names(ServiceDescriptor descriptor, Assembly assembly)
    {
        var implementationType = descriptor.IsKeyedService
            ? descriptor.KeyedImplementationType
            : descriptor.ImplementationType;
        var instance = descriptor.IsKeyedService
            ? descriptor.KeyedImplementationInstance
            : descriptor.ImplementationInstance;
        var factory = descriptor.IsKeyedService
            ? (Delegate?)descriptor.KeyedImplementationFactory
            : descriptor.ImplementationFactory;

        return _Names(descriptor.ServiceType, assembly)
            || _Names(implementationType, assembly)
            || _Names(instance?.GetType(), assembly)
            || _Names(factory?.Method.DeclaringType, assembly);
    }

    private static bool _Names(Type? type, Assembly assembly) =>
        type is not null
        && (
            type.Assembly == assembly
            || (type.IsGenericType && type.GenericTypeArguments.Any(argument => _Names(argument, assembly)))
        );

    private sealed class QueueOnlyTransportExtension : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("QueueOnly"));
            services.AddMessagingProviderCapabilities(
                MessagingProviderCapabilities.Transport(
                    "QueueOnly",
                    [MessageLane.Queue],
                    supportsIndependentLaneTopology: true
                )
            );
            services.AddSingleton<IQueueTransport, UnusedQueueTransport>();
            services.AddSingleton<IConsumerClientFactory, UnusedConsumerClientFactory>();
        }
    }

    private sealed class UnusedQueueTransport : IQueueTransport
    {
        public BrokerAddress BrokerAddress => new("QueueOnly", "localhost");

        public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The replaced queue-only transport must never send.");
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UnusedConsumerClientFactory : IConsumerClientFactory
    {
        public Task<IConsumerClient> CreateAsync(
            ConsumerClientRequest request,
            CancellationToken cancellationToken = default
        )
        {
            throw new InvalidOperationException("The replaced queue-only transport must never create a consumer.");
        }
    }
}
