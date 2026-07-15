// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupTests
{
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
