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
    public async Task should_register_distinct_bus_and_queue_transports_through_AddHeadlessMessaging()
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
}
