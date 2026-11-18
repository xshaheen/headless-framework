// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Text.Json;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tests.Registration;

/// <summary>
/// Covers how a host registers named transports with <c>setup.AddTransport(name)</c>: the name rules, the keyed
/// publishers, how a consumer is bound to one, and how the host reports each one apart.
/// </summary>
public sealed class NamedTransportRegistrationTests : TestBase
{
    private const string _Partner = "partner";
    private const string _PartnerIdentity = "orders.partner-sync";

    [Theory]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    public void should_reject_the_reserved_default_name(string name)
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddHeadlessMessaging(setup => setup.AddTransport(name));

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*reserved for the default transport*");
    }

    [Theory]
    [InlineData("partner broker")]
    [InlineData("partner\n")]
    [InlineData("")]
    public void should_reject_a_malformed_name(string name)
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddHeadlessMessaging(setup => setup.AddTransport(name));

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*letters, digits*");
    }

    [Fact]
    public void should_reject_a_name_longer_than_the_stored_column()
    {
        // given
        var services = new ServiceCollection();
        var name = new string('p', MessagingSetupBuilder.TransportNameMaxLength + 1);

        // when
        var act = () => services.AddHeadlessMessaging(setup => setup.AddTransport(name));

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_a_second_transport_of_the_same_name_in_any_case()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessMessaging(setup =>
            {
                setup.AddTransport(_Partner).UseInMemory();
                setup.AddTransport("Partner");
            });

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*already registered*");
    }

    [Fact]
    public void should_reject_a_transport_registered_without_a_provider()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessMessaging(setup =>
            {
                setup.UseInMemory();
                setup.AddTransport(_Partner);
            });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddTransport(\"partner\")*without a provider*");
    }

    [Fact]
    public void should_reject_a_second_provider_on_one_transport()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessMessaging(setup =>
            {
                var transport = setup.AddTransport(_Partner);
                transport.UseInMemory();
                transport.UseInMemory();
            });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*'partner' already has a provider*");
    }

    [Fact]
    public async Task should_resolve_a_keyed_bus_and_queue_per_named_transport_beside_the_default_ones()
    {
        // given
        await using var provider = _BuildProvider(_ => { });

        // when
        var defaultBus = provider.GetRequiredService<IBus>();
        var partnerBus = provider.GetRequiredKeyedService<IBus>(_Partner);
        var partnerQueue = provider.GetRequiredKeyedService<IQueue>(_Partner);

        // then
        partnerBus.Should().NotBeSameAs(defaultBus);
        partnerQueue.Should().NotBeNull();
    }

    [Fact]
    public void should_bind_a_consumer_from_configuration_under_the_registered_spelling()
    {
        // given
        using var provider = _BuildProvider(
            _ => { },
            ($"Headless:Messaging:Consumers:{_PartnerIdentity}:Transport", "PARTNER")
        );

        // when
        var consumer = provider
            .GetRequiredService<ConsumerRegistry>()
            .GetAll()
            .Single(x => string.Equals(x.ConsumerIdentity, _PartnerIdentity, StringComparison.Ordinal));

        // then
        consumer.TransportName.Should().Be(_Partner);
    }

    [Fact]
    public void should_bind_a_consumer_back_to_the_default_transport_by_its_reserved_name()
    {
        // given
        using var provider = _BuildProvider(setup =>
        {
            setup.Tune(_PartnerIdentity, consumer => consumer.Transport(_Partner));
            setup.Tune(_PartnerIdentity, consumer => consumer.Transport(MessagingSetupBuilder.DefaultTransportName));
        });

        // when
        var consumer = provider
            .GetRequiredService<ConsumerRegistry>()
            .GetAll()
            .Single(x => string.Equals(x.ConsumerIdentity, _PartnerIdentity, StringComparison.Ordinal));

        // then
        consumer.TransportName.Should().BeNull();
    }

    [Fact]
    public void should_reject_a_configured_transport_that_is_not_a_name()
    {
        // given
        using var provider = _BuildProvider(
            _ => { },
            ($"Headless:Messaging:Consumers:{_PartnerIdentity}:Transport", "partner broker")
        );

        // when
        var act = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Transport' must name a transport*");
    }

    [Fact]
    public void should_reject_a_responder_bound_to_a_named_transport()
    {
        // given
        using var provider = _BuildProvider(setup =>
            setup.Tune("orders.quote-desk", consumer => consumer.Transport(_Partner))
        );

        // when
        var act = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Responder 'orders.quote-desk' is bound to transport 'partner'*default transport only*");
    }

    [Fact]
    public async Task should_report_a_named_broker_apart_from_the_default_one()
    {
        // given
        await using var provider = _BuildProvider(_ => { });
        var partner = provider.GetRequiredService<MessagingTransports>().Get(_Partner);

        // when
        for (var i = 0; i < TransportConnectivity.FailureThreshold; i++)
        {
            partner.Connectivity.ReportBrokerFailure();
        }

        var report = await provider
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                registration => registration.Tags.Contains(HeadlessHealthCheckTags.Messaging),
                AbortToken
            );

        // then
        report.Entries["messaging-broker-partner"].Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries["messaging-broker"].Status.Should().Be(HealthStatus.Healthy, "the default broker answers");
    }

    [Fact]
    public async Task should_describe_the_named_transport_and_the_consumers_bound_to_it()
    {
        // given
        await using var provider = _BuildProvider(setup =>
            setup.Tune(_PartnerIdentity, consumer => consumer.Transport(_Partner))
        );

        // when
        var section = _Describe(provider);

        // then
        var partnerConsumer = section
            .GetProperty("consumers")
            .EnumerateArray()
            .Single(x =>
                string.Equals(x.GetProperty("identity").GetString(), _PartnerIdentity, StringComparison.Ordinal)
            );
        partnerConsumer.GetProperty("transport").GetString().Should().Be(_Partner);

        var internalConsumer = section
            .GetProperty("consumers")
            .EnumerateArray()
            .Single(x =>
                string.Equals(
                    x.GetProperty("identity").GetString(),
                    "orders.internal-projection",
                    StringComparison.Ordinal
                )
            );
        internalConsumer.TryGetProperty("transport", out _).Should().BeFalse("it runs on the default transport");

        var named = section
            .GetProperty("transports")
            .GetProperty("named")
            .EnumerateArray()
            .Should()
            .ContainSingle()
            .Subject;
        named.GetProperty("name").GetString().Should().Be(_Partner);
        named.GetProperty("bus").GetString().Should().NotBeNullOrEmpty();
    }

    private static JsonElement _Describe(IServiceProvider provider)
    {
        var section = provider
            .GetServices<IHostManifestSection>()
            .Single(x => string.Equals(x.Name, "messaging", StringComparison.Ordinal));
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            section.Write(writer);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static ServiceProvider _BuildProvider(
        Action<MessagingSetupBuilder> configure,
        params (string Key, string Value)[] settings
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(settings.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
                .Build()
        );
        services.ConfigureMessaging(messaging => messaging.AddModule<NamedTransportOrdersModule>());
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
            setup.AddTransport(_Partner).UseInMemory();
            configure(setup);
        });

        return services.BuildServiceProvider();
    }

    public sealed record NamedTransportOrderShipped(string OrderId);

    public sealed record NamedTransportGetQuote(string Sku);

    public sealed record NamedTransportQuote(decimal Price);

    public sealed class NamedTransportInternalProjection : IConsume<NamedTransportOrderShipped>
    {
        public ValueTask ConsumeAsync(
            ConsumeContext<NamedTransportOrderShipped> context,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }

    public sealed class NamedTransportPartnerSync : IConsume<NamedTransportOrderShipped>
    {
        public ValueTask ConsumeAsync(
            ConsumeContext<NamedTransportOrderShipped> context,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }

    public sealed class NamedTransportQuoteDesk : IRespond<NamedTransportGetQuote, NamedTransportQuote>
    {
        public ValueTask<NamedTransportQuote> RespondAsync(
            ConsumeContext<NamedTransportGetQuote> context,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(new NamedTransportQuote(1));
    }

    public sealed class NamedTransportOrdersModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog)
        {
            catalog.AddBusConsumer<NamedTransportInternalProjection, NamedTransportOrderShipped>(
                "orders.internal-projection",
                everyInstance: false,
                static (_, _, _) => ValueTask.CompletedTask
            );
            catalog.AddBusConsumer<NamedTransportPartnerSync, NamedTransportOrderShipped>(
                _PartnerIdentity,
                everyInstance: false,
                static (_, _, _) => ValueTask.CompletedTask
            );
            catalog.AddQueueResponder<NamedTransportQuoteDesk, NamedTransportGetQuote, NamedTransportQuote>(
                "orders.quote-desk",
                static (_, _, _) => ValueTask.CompletedTask
            );
        }
    }
}
