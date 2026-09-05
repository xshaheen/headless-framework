// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_snapshot_session_affinity_without_resolving_clients(bool sessions)
    {
        var effects = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseAzureServiceBus(options =>
            {
                options.ConnectionString =
                    "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey";
                options.EnableSessions = sessions;
            });
            setup.Queue.ForMessage<AffinityContract>(message => message.Contract("orders").RequireRoutingAffinity());
        });
        services.AddSingleton<IAzureServiceBusClientPool>(_ =>
        {
            effects++;
            return Substitute.For<IAzureServiceBusClientPool>();
        });
        services.AddSingleton<IQueueTransport>(_ =>
        {
            effects++;
            return Substitute.For<IQueueTransport>();
        });
        await using var provider = services.BuildServiceProvider();

        var model = provider.GetRequiredService<IMessagingCapabilityModel>();
        model.Providers.Single().RoutingAffinityRoutes.Any().Should().Be(sessions);
        if (!sessions)
        {
            var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
            await act.Should().ThrowAsync<MessagingConfigurationException>().WithMessage("*affinity*unsupported*");
        }

        effects.Should().Be(0);
    }

    private sealed record AffinityContract;

    [Fact]
    public async Task should_register_distinct_bus_and_queue_transports_through_add_headless_messaging()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(setup =>
            setup.UseAzureServiceBus(options =>
            {
                options.ConnectionString =
                    "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey";
            })
        );

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBusTransport>().Should().BeOfType<AzureServiceBusTransport>();
        provider.GetRequiredService<IQueueTransport>().Should().BeOfType<AzureServiceBusQueueTransport>();
        provider.GetRequiredService<IConsumerClientFactory>().Should().BeOfType<AzureServiceBusConsumerClientFactory>();
        provider
            .GetRequiredService<IOptions<AzureServiceBusMessagingOptions>>()
            .Value.ConnectionString.Should()
            .Contain("mynamespace");
    }

    [Fact]
    public async Task should_share_single_client_pool_between_bus_and_queue_transports()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(setup =>
            setup.UseAzureServiceBus(options =>
            {
                options.ConnectionString =
                    "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey";
            })
        );

        await using var provider = services.BuildServiceProvider();

        // co-registering bus and queue must not create independent client pools
        var pool = provider.GetRequiredService<IAzureServiceBusClientPool>();
        pool.Should().BeOfType<AzureServiceBusClientPool>();
        provider.GetRequiredService<IAzureServiceBusClientPool>().Should().BeSameAs(pool);

        _ = provider.GetRequiredService<IBusTransport>();
        _ = provider.GetRequiredService<IQueueTransport>();
    }
}
