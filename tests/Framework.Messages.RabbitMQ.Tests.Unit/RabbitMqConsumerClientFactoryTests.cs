// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages;
using Framework.Messages.Transport;
using Framework.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class RabbitMqConsumerClientFactoryTests : TestBase
{
    [Fact]
    public async Task should_create_consumer_client()
    {
        // Given
        var pool = Substitute.For<IConnectionChannelPool>();
        var options = Options.Create(new RabbitMQOptions { HostName = "localhost", Port = 5672 });
        var serviceProvider = Substitute.For<IServiceProvider>();

        var factory = new RabbitMqConsumerClientFactory(options, pool, serviceProvider);

        // When
        var client = await factory.CreateAsync("test-group", 5);

        // Then
        client.Should().NotBeNull();
        client.Should().BeOfType<RabbitMqConsumerClient>();
    }
}
