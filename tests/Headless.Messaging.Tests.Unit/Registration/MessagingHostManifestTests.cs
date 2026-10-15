// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Text.Json;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Registration;

/// <summary>
/// Covers the <c>messaging</c> section of the host manifest: one entry per consumer identity and lane, read from the
/// host's frozen registry with the tuning and <c>ConsumeOnly</c> filter the host runs with.
/// </summary>
public sealed class MessagingHostManifestTests : TestBase
{
    private const string _Orders = "orders";
    private const string _OrderPlaced = "orders.order-placed";
    private const string _OrderCancelled = "orders.order-cancelled";

    [Fact]
    public void should_print_concurrency_bound_from_configuration_without_tune()
    {
        // Tuning applied from configuration shows up in the manifest exactly as the host runs it.
        using var provider = _BuildProvider(
            setup => setup.AddModule<DescribedOrdersModule>(),
            ($"Headless:Messaging:Consumers:{_Orders}:Concurrency", "8")
        );

        var orders = _Consumer(_Describe(provider), _Orders, "bus");

        orders.GetProperty("concurrency").GetInt32().Should().Be(8);
    }

    [Fact]
    public void should_print_run_state_from_consume_only_and_always_run_every_instance_consumers()
    {
        // An every-instance consumer runs on every host, so ConsumeOnly neither stops it nor is reported for it.
        using var provider = _BuildProvider(setup =>
            setup
                .AddModule<DescribedOrdersModule>()
                .AddModule<BillingModule>()
                .AddModule<BillingPriceCacheModule>()
                .ConsumeOnly(_Orders)
        );

        var section = _Describe(provider);

        var invoices = _Consumer(section, TestConsumers.InvoiceProjection, "bus");
        invoices.GetProperty("delivery").GetString().Should().Be("competing");
        invoices.GetProperty("runState").GetString().Should().Be("registered-not-run-here");
        invoices.GetProperty("consumeOnly").GetString().Should().Be("not-listed");

        var priceCache = _Consumer(section, TestConsumers.PriceCache, "bus");
        priceCache.GetProperty("delivery").GetString().Should().Be("every-instance");
        priceCache.GetProperty("runState").GetString().Should().Be("runs-here");
        priceCache.TryGetProperty("consumeOnly", out _).Should().BeFalse();
        priceCache.TryGetProperty("failurePolicy", out _).Should().BeFalse();
        priceCache.TryGetProperty("inboxRetentionSeconds", out _).Should().BeFalse();

        var orders = _Consumer(section, _Orders, "bus");
        orders.GetProperty("runState").GetString().Should().Be("runs-here");
        orders.GetProperty("consumeOnly").GetString().Should().Be("listed");
    }

    [Fact]
    public void should_omit_the_consume_only_state_when_the_host_has_no_filter()
    {
        using var provider = _BuildProvider(setup => setup.AddModule<BillingModule>());

        var invoices = _Consumer(_Describe(provider), TestConsumers.InvoiceProjection, "bus");

        invoices.GetProperty("runState").GetString().Should().Be("runs-here");
        invoices.TryGetProperty("consumeOnly", out _).Should().BeFalse();
    }

    [Fact]
    public void should_list_every_message_of_one_identity_with_its_contract_version_under_one_entry()
    {
        using var provider = _BuildProvider(
            setup => setup.AddModule<DescribedOrdersModule>(),
            configureServices: services =>
                services.ConfigureMessaging(messaging =>
                {
                    messaging.Message<DescribedOrderPlaced>(_OrderPlaced, "2");
                    messaging.Message<DescribedOrderCancelled>(_OrderCancelled, "1");
                })
        );

        var orders = _Consumer(_Describe(provider), _Orders, "bus");

        orders
            .GetProperty("messages")
            .EnumerateArray()
            .Select(x => (x.GetProperty("name").GetString(), x.GetProperty("contractVersion").GetString()))
            .Should()
            .Equal((_OrderCancelled, "1"), (_OrderPlaced, "2"));
    }

    [Fact]
    public void should_print_one_identity_on_the_bus_and_queue_lanes_as_two_entries_in_ordinal_order()
    {
        using var provider = _BuildProvider(setup =>
            setup.AddModule<DescribedOrdersModule>().AddModule<BillingModule>()
        );

        var entries = _Describe(provider)
            .GetProperty("consumers")
            .EnumerateArray()
            .Select(x => (x.GetProperty("identity").GetString(), x.GetProperty("lane").GetString()))
            .ToList();

        entries.Should().Equal((TestConsumers.InvoiceProjection, "bus"), (_Orders, "bus"), (_Orders, "queue"));
    }

    [Fact]
    public void should_print_the_failure_policy_inbox_retention_circuit_breaker_and_middleware_the_host_runs_with()
    {
        using var provider = _BuildProvider(
            setup =>
                setup
                    .AddModule<DescribedOrdersModule>()
                    .Tune(
                        _Orders,
                        consumer =>
                            consumer
                                .FailurePolicy(policy =>
                                    policy.Immediate(2).Delayed(3, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1))
                                )
                                .InboxRetention(TimeSpan.FromDays(7))
                                .CircuitBreaker(breaker => breaker.FailureThreshold = 4)
                                .UseMiddleware<AuditMiddleware>()
                    ),
            ($"Headless:Messaging:Consumers:{_Orders}:FailurePolicy:ImmediateRetries", "5")
        );

        var orders = _Consumer(_Describe(provider), _Orders, "bus");

        var failurePolicy = orders.GetProperty("failurePolicy");
        failurePolicy.GetProperty("immediateRetries").GetInt32().Should().Be(5);
        failurePolicy.GetProperty("delayedRetries").GetInt32().Should().Be(3);
        failurePolicy.GetProperty("delayedInitialDelaySeconds").GetDouble().Should().Be(5);
        failurePolicy.GetProperty("delayedMaxDelaySeconds").GetDouble().Should().Be(60);
        orders.GetProperty("inboxRetentionSeconds").GetInt64().Should().Be(7 * 24 * 60 * 60);
        var circuitBreaker = orders.GetProperty("circuitBreaker");
        circuitBreaker.GetProperty("enabled").GetBoolean().Should().BeTrue();
        circuitBreaker.GetProperty("failureThreshold").GetInt32().Should().Be(4);
        circuitBreaker.TryGetProperty("openDurationSeconds", out _).Should().BeFalse();
        orders
            .GetProperty("middleware")
            .EnumerateArray()
            .Select(x => x.GetString())
            .Should()
            .Equal(typeof(AuditMiddleware).FullName);
        orders.TryGetProperty("rateLimit", out _).Should().BeFalse();
    }

    [Fact]
    public void should_omit_overrides_a_consumer_was_not_given()
    {
        using var provider = _BuildProvider(setup => setup.AddModule<BillingModule>());

        var invoices = _Consumer(_Describe(provider), TestConsumers.InvoiceProjection, "bus");

        invoices.TryGetProperty("circuitBreaker", out _).Should().BeFalse();
        invoices.TryGetProperty("rateLimit", out _).Should().BeFalse();
        invoices.TryGetProperty("middleware", out _).Should().BeFalse();
        invoices.GetProperty("concurrency").GetInt32().Should().Be(1);
        invoices.GetProperty("inboxRetentionSeconds").GetInt64().Should().Be(30 * 24 * 60 * 60);
        invoices
            .GetProperty("failurePolicy")
            .GetProperty("immediateRetries")
            .ValueKind.Should()
            .Be(JsonValueKind.Number);
    }

    [Fact]
    public void should_print_the_effective_rate_limit()
    {
        using var provider = _BuildProvider(
            setup => setup.AddModule<BillingModule>(),
            ($"Headless:Messaging:Consumers:{TestConsumers.InvoiceProjection}:RateLimit:TokenBucket:TokenLimit", "20"),
            (
                $"Headless:Messaging:Consumers:{TestConsumers.InvoiceProjection}:RateLimit:TokenBucket:TokensPerPeriod",
                "5"
            ),
            (
                $"Headless:Messaging:Consumers:{TestConsumers.InvoiceProjection}:RateLimit:TokenBucket:ReplenishmentPeriod",
                "00:00:02"
            )
        );

        var rateLimit = _Consumer(_Describe(provider), TestConsumers.InvoiceProjection, "bus").GetProperty("rateLimit");

        rateLimit.GetProperty("algorithm").GetString().Should().Be("token-bucket");
        rateLimit.GetProperty("tokenLimit").GetInt32().Should().Be(20);
        rateLimit.GetProperty("tokensPerPeriod").GetInt32().Should().Be(5);
        rateLimit.GetProperty("replenishmentPeriodSeconds").GetDouble().Should().Be(2);
    }

    [Fact]
    public void should_print_a_fixed_window_rate_limit_set_by_tune()
    {
        using var provider = _BuildProvider(setup =>
            setup
                .AddModule<BillingModule>()
                .Tune(
                    TestConsumers.InvoiceProjection,
                    consumer => consumer.RateLimit(ConsumerRateLimit.FixedWindow(100, TimeSpan.FromSeconds(10)))
                )
        );

        var rateLimit = _Consumer(_Describe(provider), TestConsumers.InvoiceProjection, "bus").GetProperty("rateLimit");

        rateLimit.GetProperty("algorithm").GetString().Should().Be("fixed-window");
        rateLimit.GetProperty("permitLimit").GetInt32().Should().Be(100);
        rateLimit.GetProperty("windowSeconds").GetDouble().Should().Be(10);
        rateLimit.TryGetProperty("tokenLimit", out _).Should().BeFalse();
    }

    [Fact]
    public void should_never_print_provider_specific_consumer_configuration()
    {
        using var provider = _BuildProvider(setup =>
            setup
                .AddModule<BillingModule>()
                .Tune(
                    TestConsumers.InvoiceProjection,
                    consumer =>
                        ((IConsumerProviderConfigBuilder)consumer).SetConsumerProviderConfig(
                            new SecretProviderConfig("amqp://user:hunter2@broker")
                        )
                )
        );

        var section = _Describe(provider);

        section.GetRawText().Should().NotContain("hunter2").And.NotContain("broker").And.NotContain("SecretProvider");
    }

    [Fact]
    public async Task should_describe_the_publish_side_with_contract_lanes_transports_and_outboxes()
    {
        // The in-memory transports dispose asynchronously only, and describing resolves them.
        await using var provider = _BuildProvider(
            setup => setup.UseInMemory(),
            configureServices: services =>
                services.ConfigureMessaging(messaging =>
                    messaging
                        .Message<DescribedOrderPlaced>(_OrderPlaced, "2")
                        .OnBus(bus => bus.WithDeliveryMode(DeliveryMode.Direct))
                )
        );

        var section = _Describe(provider);

        var message = section.GetProperty("messages").EnumerateArray().Should().ContainSingle().Subject;
        message.GetProperty("name").GetString().Should().Be(_OrderPlaced);
        message.GetProperty("contractVersion").GetString().Should().Be("2");
        var bus = message
            .GetProperty("lanes")
            .EnumerateArray()
            .Should()
            .ContainSingle(x => x.GetProperty("lane").GetString() == "bus")
            .Subject;
        bus.GetProperty("deliveryMode").GetString().Should().Be("direct");

        section.GetProperty("transports").GetProperty("bus").GetString().Should().NotBeNullOrEmpty();
        section.GetProperty("outboxes").GetArrayLength().Should().Be(0, "a host without storage has no outbox");
    }

    private static JsonElement _Describe(IServiceProvider provider)
    {
        var section = provider
            .GetServices<IHostManifestSection>()
            .Should()
            .ContainSingle(x => x.Name == "messaging")
            .Subject;

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            section.Write(writer);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static JsonElement _Consumer(JsonElement section, string identity, string lane) =>
        section
            .GetProperty("consumers")
            .EnumerateArray()
            .Should()
            .ContainSingle(x =>
                x.GetProperty("identity").GetString() == identity && x.GetProperty("lane").GetString() == lane
            )
            .Subject;

    private static ServiceProvider _BuildProvider(
        Action<MessagingSetupBuilder> configure,
        params (string Key, string Value)[] settings
    ) => _BuildProvider(configure, configureServices: null, settings);

    private static ServiceProvider _BuildProvider(
        Action<MessagingSetupBuilder> configure,
        Action<IServiceCollection>? configureServices,
        params (string Key, string Value)[] settings
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HostControlProbe>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(settings.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
                .Build()
        );
        configureServices?.Invoke(services);
        services.AddHeadlessMessaging(configure);

        return services.BuildServiceProvider();
    }

    private sealed record SecretProviderConfig(string Address);

    public sealed record DescribedOrderPlaced(string OrderId);

    public sealed record DescribedOrderCancelled(string OrderId);

    public sealed record DescribedShipOrder(string OrderId);

    public sealed class DescribedOrdersProjection : IConsume<DescribedOrderPlaced>, IConsume<DescribedOrderCancelled>
    {
        public ValueTask ConsumeAsync(
            ConsumeContext<DescribedOrderPlaced> context,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;

        public ValueTask ConsumeAsync(
            ConsumeContext<DescribedOrderCancelled> context,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }

    public sealed class DescribedShipOrderHandler : IConsume<DescribedShipOrder>
    {
        public ValueTask ConsumeAsync(
            ConsumeContext<DescribedShipOrder> context,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }

    /// <summary>One identity that consumes two Bus messages and also owns a Queue message.</summary>
    public sealed class DescribedOrdersModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog)
        {
            catalog.AddBusConsumer<DescribedOrdersProjection, DescribedOrderPlaced>(
                _Orders,
                everyInstance: false,
                TestConsumers.Dispatch<DescribedOrdersProjection, DescribedOrderPlaced>()
            );
            catalog.AddBusConsumer<DescribedOrdersProjection, DescribedOrderCancelled>(
                _Orders,
                everyInstance: false,
                TestConsumers.Dispatch<DescribedOrdersProjection, DescribedOrderCancelled>()
            );
            catalog.AddQueueConsumer<DescribedShipOrderHandler, DescribedShipOrder>(
                _Orders,
                TestConsumers.Dispatch<DescribedShipOrderHandler, DescribedShipOrder>()
            );
        }
    }
}
