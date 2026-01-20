// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages;
using Framework.Messages.Transport;
using Framework.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class RabbitMQConsumerClientFactoryTests : TestBase
{
    [Fact]
    public void should_create_consumer_client()
    {
        // Given
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionChannelPool, ConnectionChannelPool>();
        services.AddSingleton<IOptions<MessagingOptions>>(Options.Create(new MessagingOptions { Version = "v1" }));
        services.AddSingleton<IOptions<RabbitMQOptions>>(
            Options.Create(new RabbitMQOptions { HostName = "localhost", Port = 5672 })
        );

        var serviceProvider = services.BuildServiceProvider();
        var factory = new RabbitMQConsumerClientFactory(serviceProvider);

        // When
        var client = factory.Create("test-group", 5);

        // Then
        client.Should().NotBeNull();
        client.Should().BeOfType<RabbitMQConsumerClient>();
    }
}
