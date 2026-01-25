// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Exceptions;
using Headless.Messaging.RabbitMq;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;
using RabbitMQ.Client;

namespace Tests;

public sealed class RabbitMqConsumerClientFactoryTests : TestBase
{
    [Fact]
    public async Task should_create_consumer_client()
    {
        // given
        var pool = Substitute.For<IConnectionChannelPool>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();

        pool.Exchange.Returns("test.exchange");
        pool.GetConnectionAsync().Returns(connection);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>()).Returns(channel);

        var options = Options.Create(new RabbitMqOptions { HostName = "localhost", Port = 5672 });
        var serviceProvider = Substitute.For<IServiceProvider>();

        var factory = new RabbitMqConsumerClientFactory(options, pool, serviceProvider);

        // when
        var client = await factory.CreateAsync("test-group", 5);

        // then
        client.Should().NotBeNull();
        client.Should().BeOfType<RabbitMqConsumerClient>();
    }

    [Fact]
    public async Task should_throw_broker_connection_exception_when_connection_fails()
    {
        // given
        var pool = Substitute.For<IConnectionChannelPool>();
        pool.Exchange.Returns("test.exchange");
        pool.GetConnectionAsync().ThrowsAsync(new InvalidOperationException("Connection failed"));

        var options = Options.Create(new RabbitMqOptions { HostName = "localhost", Port = 5672 });
        var serviceProvider = Substitute.For<IServiceProvider>();

        var factory = new RabbitMqConsumerClientFactory(options, pool, serviceProvider);

        // when
        var act = () => factory.CreateAsync("test-group", 5);

        // then
        await act.Should().ThrowAsync<BrokerConnectionException>();
    }

    [Fact]
    public async Task should_wrap_inner_exception_in_broker_connection_exception()
    {
        // given
        var pool = Substitute.For<IConnectionChannelPool>();
        var innerException = new TimeoutException("Connection timed out");
        pool.Exchange.Returns("test.exchange");
        pool.GetConnectionAsync().ThrowsAsync(innerException);

        var options = Options.Create(new RabbitMqOptions { HostName = "localhost", Port = 5672 });
        var serviceProvider = Substitute.For<IServiceProvider>();

        var factory = new RabbitMqConsumerClientFactory(options, pool, serviceProvider);

        // when
        var act = () => factory.CreateAsync("test-group", 5);

        // then
        var exception = await act.Should().ThrowAsync<BrokerConnectionException>();
        exception.Which.InnerException.Should().BeSameAs(innerException);
    }

    [Fact]
    public async Task should_connect_client_before_returning()
    {
        // given
        var pool = Substitute.For<IConnectionChannelPool>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();

        pool.Exchange.Returns("test.exchange");
        pool.GetConnectionAsync().Returns(connection);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>()).Returns(channel);

        var options = Options.Create(new RabbitMqOptions { HostName = "localhost", Port = 5672 });
        var serviceProvider = Substitute.For<IServiceProvider>();

        var factory = new RabbitMqConsumerClientFactory(options, pool, serviceProvider);

        // when
        await factory.CreateAsync("test-group", 5);

        // then - verify connection was retrieved during factory.CreateAsync
        await pool.Received(1).GetConnectionAsync();
    }
}
