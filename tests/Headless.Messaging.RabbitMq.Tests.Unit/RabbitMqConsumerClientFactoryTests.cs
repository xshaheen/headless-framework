// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Exceptions;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
        pool.GetConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>()).Returns(channel);

        var options = Options.Create(
            new RabbitMqMessagingOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "test_user",
                Password = "test_pass",
            }
        );
        var serviceProvider = Substitute.For<IServiceProvider>();

        var factory = new RabbitMqConsumerClientFactory(options, pool, serviceProvider);

        // when
        var client = await factory.CreateAsync(
            new ConsumerClientRequest("test-group", 5, MessageLane.Queue),
            AbortToken
        );

        // then
        client.Should().NotBeNull();
        client.Should().BeOfType<RabbitMqConsumerClient>();
    }

    [Fact]
    public async Task should_declare_every_instance_support_on_the_transport_capability()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.UseRabbitMq(options => options.HostName = "localhost"));
        await using var provider = services.BuildServiceProvider();

        provider
            .GetServices<MessagingProviderCapabilities>()
            .Single(x => x.Role == MessagingProviderRole.Transport)
            .SupportsEveryInstance.Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_release_owned_connection_when_every_instance_client_fails_to_connect()
    {
        // given
        var pool = Substitute.For<IConnectionChannelPool>();
        var ownedConnection = Substitute.For<IConnection>();
        pool.Exchange.Returns("test.exchange");
        pool.CreateNonRecoveringConnectionAsync(Arg.Any<CancellationToken>()).Returns(ownedConnection);
        ownedConnection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("channel failed"));
        var factory = new RabbitMqConsumerClientFactory(
            Options.Create(
                new RabbitMqMessagingOptions
                {
                    HostName = "localhost",
                    UserName = "test_user",
                    Password = "test_pass",
                }
            ),
            pool,
            Substitute.For<IServiceProvider>()
        );
        var request = new ConsumerClientRequest(
            "billing.cache",
            1,
            MessageLane.Bus,
            ConsumerSubscriptionKind.EveryInstance,
            Guid.NewGuid()
        );

        // when
        var act = () => factory.CreateAsync(request, AbortToken);

        // then
        await act.Should().ThrowAsync<BrokerConnectionException>();
        await ownedConnection.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task should_throw_broker_connection_exception_when_connection_fails()
    {
        // given
        var pool = Substitute.For<IConnectionChannelPool>();
        pool.Exchange.Returns("test.exchange");
        pool.GetConnectionAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        var options = Options.Create(
            new RabbitMqMessagingOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "test_user",
                Password = "test_pass",
            }
        );
        var serviceProvider = Substitute.For<IServiceProvider>();

        var factory = new RabbitMqConsumerClientFactory(options, pool, serviceProvider);

        // when
        var act = () => factory.CreateAsync(new ConsumerClientRequest("test-group", 5, MessageLane.Queue));

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
        pool.GetConnectionAsync(Arg.Any<CancellationToken>()).ThrowsAsync(innerException);

        var options = Options.Create(
            new RabbitMqMessagingOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "test_user",
                Password = "test_pass",
            }
        );
        var serviceProvider = Substitute.For<IServiceProvider>();

        var factory = new RabbitMqConsumerClientFactory(options, pool, serviceProvider);

        // when
        var act = () => factory.CreateAsync(new ConsumerClientRequest("test-group", 5, MessageLane.Queue));

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
        pool.GetConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>()).Returns(channel);

        var options = Options.Create(
            new RabbitMqMessagingOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "test_user",
                Password = "test_pass",
            }
        );
        var serviceProvider = Substitute.For<IServiceProvider>();

        var factory = new RabbitMqConsumerClientFactory(options, pool, serviceProvider);

        // when
        await factory.CreateAsync(new ConsumerClientRequest("test-group", 5, MessageLane.Queue), AbortToken);

        // then - verify connection was retrieved during factory.CreateAsync
        await pool.Received(1).GetConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_propagate_exact_token_to_connection_setup()
    {
        var pool = Substitute.For<IConnectionChannelPool>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        pool.Exchange.Returns("test.exchange");
        pool.GetConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>()).Returns(channel);
        var factory = new RabbitMqConsumerClientFactory(
            Options.Create(
                new RabbitMqMessagingOptions
                {
                    HostName = "localhost",
                    UserName = "guest",
                    Password = "guest",
                }
            ),
            pool,
            Substitute.For<IServiceProvider>()
        );
        using var cts = new CancellationTokenSource();

        await factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue), cts.Token);

        await pool.Received(1).GetConnectionAsync(cts.Token);
    }

    [Fact]
    public async Task should_not_wrap_connection_cancellation()
    {
        var pool = Substitute.For<IConnectionChannelPool>();
        pool.Exchange.Returns("test.exchange");
        pool.GetConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(call => Task.FromCanceled<IConnection>(call.Arg<CancellationToken>()));
        var factory = new RabbitMqConsumerClientFactory(
            Options.Create(
                new RabbitMqMessagingOptions
                {
                    HostName = "localhost",
                    UserName = "guest",
                    Password = "guest",
                }
            ),
            pool,
            Substitute.For<IServiceProvider>()
        );
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () =>
            await factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_finish_within_host_shutdown_timeout_when_unavailable_broker_startup()
    {
        var rabbitOptions = Options.Create(
            new RabbitMqMessagingOptions
            {
                HostName = "unavailable",
                UserName = "guest",
                Password = "guest",
            }
        );
        await using var pool = new ConnectionChannelPool(
            NullLogger<ConnectionChannelPool>.Instance,
            Options.Create(new MessagingOptions { Version = "v1" }),
            rabbitOptions,
            async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return null!;
            }
        );
        var factory = new RabbitMqConsumerClientFactory(rabbitOptions, pool, Substitute.For<IServiceProvider>());
        var hostShutdownTimeout = TimeSpan.FromSeconds(1);
        using var hostCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var startup = factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue), hostCts.Token);
        var act = async () => await startup.WaitAsync(hostShutdownTimeout, AbortToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
