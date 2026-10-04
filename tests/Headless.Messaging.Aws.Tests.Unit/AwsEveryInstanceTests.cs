// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// AWS refuses every-instance Bus delivery: SQS has no idle deletion, so a crashed process would leave its queue and
/// SNS subscription behind. The refusal happens at startup, before any consumer client or broker object exists.
/// </summary>
public sealed class AwsEveryInstanceTests : TestBase
{
    [Fact]
    public async Task should_not_declare_every_instance_support()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.UseAws(options => options.Region = Amazon.RegionEndpoint.USEast1));
        await using var provider = services.BuildServiceProvider();

        // when
        var capabilities = provider.GetRequiredService<MessagingProviderCapabilities>();

        // then
        capabilities.SupportsEveryInstance.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_startup_naming_the_provider_and_the_consumer_before_any_client_exists()
    {
        // given
        var effects = 0;
        var clientFactory = Substitute.For<IConsumerClientFactory>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => messaging.AddModule<PriceCacheModule>());
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseAws(options => options.Region = Amazon.RegionEndpoint.USEast1);
            setup.UseInMemoryStorage();
        });
        services.AddSingleton(clientFactory);
        services.AddSingleton<IBusTransport>(_ =>
        {
            effects++;
            return Substitute.For<IBusTransport>();
        });
        services.AddSingleton<IQueueTransport>(_ =>
        {
            effects++;
            return Substitute.For<IQueueTransport>();
        });
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage($"*'{PriceCacheModule.Identity}'*every-instance*'Amazon SQS'*");
        await clientFactory.DidNotReceiveWithAnyArgs().CreateAsync(default!, AbortToken);
        effects.Should().Be(0);
    }

    [Fact]
    public async Task should_refuse_an_every_instance_client_naming_the_provider_and_the_consumer()
    {
        // given
        var factory = new AmazonSqsConsumerClientFactory(
            Options.Create(new AmazonSqsMessagingOptions { Region = Amazon.RegionEndpoint.USEast1 }),
            Substitute.For<ILogger<AmazonSqsConsumerClient>>()
        );
        var request = new ConsumerClientRequest(
            PriceCacheModule.Identity,
            1,
            MessageLane.Bus,
            ConsumerSubscriptionKind.EveryInstance,
            Guid.NewGuid()
        );

        // when
        var act = async () => await factory.CreateAsync(request, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<NotSupportedException>()
            .WithMessage($"*'{PriceCacheModule.Identity}'*'Amazon SQS'*");
    }
}

public sealed record PriceChanged(string Sku);

public sealed class PriceCache : IConsume<PriceChanged>
{
    public ValueTask ConsumeAsync(ConsumeContext<PriceChanged> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

public sealed class PriceCacheModule : IMessagingModule
{
    public const string Identity = "pricing.price-cache";

    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddBusConsumer<PriceCache, PriceChanged>(
            Identity,
            everyInstance: true,
            static (services, context, cancellationToken) =>
                ActivatorUtilities
                    .CreateInstance<PriceCache>(services)
                    .ConsumeAsync((ConsumeContext<PriceChanged>)context, cancellationToken)
        );
    }
}
