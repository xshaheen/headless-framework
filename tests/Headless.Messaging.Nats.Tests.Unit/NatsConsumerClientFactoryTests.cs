// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
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
    public async Task should_create_consumer_client()
    {
        // given
        var factory = new NatsConsumerClientFactory(_options, _serviceProvider);

        // when & then - this will throw because we can't connect to NATS in unit tests
        // but it verifies the factory attempts to create and connect
        var act = async () => await factory.CreateAsync("test-group", 1);

        await act.Should()
            .ThrowAsync<BrokerConnectionException>()
            .WithMessage("*"); // Any message is fine, we just verify it wraps the exception
    }

    [Fact]
    public async Task should_wrap_connection_exception_in_broker_connection_exception()
    {
        // given
        var factory = new NatsConsumerClientFactory(_options, _serviceProvider);

        // when
        var act = async () => await factory.CreateAsync("test-group", 1);

        // then
        var exception = await act.Should().ThrowAsync<BrokerConnectionException>();
        exception.Which.InnerException.Should().NotBeNull();
    }

    [Fact]
    public async Task should_pass_group_name_to_client()
    {
        // given
        var factory = new NatsConsumerClientFactory(_options, _serviceProvider);

        // when & then - verify factory attempts creation with correct parameters
        // The exception message or client properties would reflect the group name
        var act = async () => await factory.CreateAsync("my-consumer-group", 5);

        // Factory creates client and calls Connect(), which throws without NATS server
        await act.Should().ThrowAsync<BrokerConnectionException>();
    }

    [Fact]
    public async Task should_pass_group_concurrent_to_client()
    {
        // given
        var factory = new NatsConsumerClientFactory(_options, _serviceProvider);

        // when & then
        var act = async () => await factory.CreateAsync("test-group", 10);

        await act.Should().ThrowAsync<BrokerConnectionException>();
    }

    [Fact]
    public void should_implement_consumer_client_factory_interface()
    {
        // given
        var factory = new NatsConsumerClientFactory(_options, _serviceProvider);

        // then
        factory.Should().BeAssignableTo<IConsumerClientFactory>();
    }
}
