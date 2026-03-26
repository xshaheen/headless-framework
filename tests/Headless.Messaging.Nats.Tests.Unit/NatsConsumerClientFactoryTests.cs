// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class NatsConsumerClientFactoryTests : TestBase
{
    private readonly IOptions<MessagingNatsOptions> _options;
    private readonly IServiceProvider _serviceProvider;

    public NatsConsumerClientFactoryTests()
    {
        _options = Options.Create(new MessagingNatsOptions { Servers = "nats://localhost:4222" });
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
    }

    [Fact]
    public async Task should_wrap_connection_exception_in_broker_connection_exception()
    {
        var factory = new NatsConsumerClientFactory(_options, _serviceProvider);

        // ConnectAsync will fail without a real NATS server
        var act = async () => await factory.CreateAsync("test-group", 1);

        var exception = await act.Should().ThrowAsync<BrokerConnectionException>();
        exception.Which.InnerException.Should().NotBeNull();
    }

    [Fact]
    public void should_implement_consumer_client_factory_interface()
    {
        var factory = new NatsConsumerClientFactory(_options, _serviceProvider);
        factory.Should().BeAssignableTo<IConsumerClientFactory>();
    }
}
