// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Nats;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client;
using NSubstitute;
using MsOptions = Microsoft.Extensions.Options;

namespace Tests;

public sealed class NatsConnectionPoolTests : TestBase
{
    private readonly ILogger<NatsConnectionPool> _logger;
    private readonly MsOptions.IOptions<MessagingNatsOptions> _options;

    public NatsConnectionPoolTests()
    {
        _logger = NullLogger<NatsConnectionPool>.Instance;
        _options = MsOptions.Options.Create(
            new MessagingNatsOptions { Servers = "nats://localhost:4222", ConnectionPoolSize = 10 }
        );
    }

    [Fact]
    public void should_initialize_with_correct_server_address()
    {
        // given, when
        using var pool = new NatsConnectionPool(_logger, _options);

        // then
        pool.ServersAddress.Should().Be("nats://localhost:4222");
    }

    [Fact]
    public void should_return_connection_to_pool_when_connected()
    {
        // given
        using var pool = new NatsConnectionPool(_logger, _options);
        var connection = Substitute.For<IConnection>();
        connection.State.Returns(ConnState.CONNECTED);

        // when
        var result = pool.Return(connection);

        // then
        result.Should().BeTrue();
        connection.DidNotReceive().Dispose();
    }

    [Fact]
    public void should_dispose_connection_when_not_connected()
    {
        // given
        using var pool = new NatsConnectionPool(_logger, _options);
        var connection = Substitute.For<IConnection>();
        connection.State.Returns(ConnState.DISCONNECTED);
        connection.IsReconnecting().Returns(false);

        // when
        var result = pool.Return(connection);

        // then
        result.Should().BeFalse();
        connection.Received(1).Dispose();
    }

    [Fact]
    public void should_not_dispose_reconnecting_connection()
    {
        // given
        using var pool = new NatsConnectionPool(_logger, _options);
        var connection = Substitute.For<IConnection>();
        connection.State.Returns(ConnState.RECONNECTING);
        connection.IsReconnecting().Returns(true);

        // when
        var result = pool.Return(connection);

        // then
        result.Should().BeFalse();
        connection.DidNotReceive().Dispose();
    }

    [Fact]
    public void should_dispose_all_connections_on_pool_dispose()
    {
        // given
        var pool = new NatsConnectionPool(_logger, _options);
        var connection1 = Substitute.For<IConnection>();
        var connection2 = Substitute.For<IConnection>();
        connection1.State.Returns(ConnState.CONNECTED);
        connection2.State.Returns(ConnState.CONNECTED);

        pool.Return(connection1);
        pool.Return(connection2);

        // when
        pool.Dispose();

        // then
        connection1.Received(1).Dispose();
        connection2.Received(1).Dispose();
    }

    [Fact]
    public void should_not_accept_more_connections_than_pool_size()
    {
        // given
        var options = MsOptions.Options.Create(
            new MessagingNatsOptions { Servers = "nats://localhost:4222", ConnectionPoolSize = 2 }
        );
        using var pool = new NatsConnectionPool(_logger, options);

        var connections = Enumerable
            .Range(0, 5)
            .Select(_ =>
            {
                var conn = Substitute.For<IConnection>();
                conn.State.Returns(ConnState.CONNECTED);
                conn.IsReconnecting().Returns(false);
                return conn;
            })
            .ToList();

        // when
        var results = connections.Select(c => pool.Return(c)).ToList();

        // then
        results.Take(2).Should().AllBeEquivalentTo(true, "First 2 connections should be pooled");
        results.Skip(2).Should().AllBeEquivalentTo(false, "Excess connections should be rejected");
    }

    [Fact]
    public void should_reuse_connections_from_pool()
    {
        // given
        using var pool = new NatsConnectionPool(_logger, _options);
        var connection = Substitute.For<IConnection>();
        connection.State.Returns(ConnState.CONNECTED);

        pool.Return(connection);

        // when - we can't actually call RentConnection without a real NATS server
        // but we can verify the pool accepts and tracks connections
        var connection2 = Substitute.For<IConnection>();
        connection2.State.Returns(ConnState.CONNECTED);
        var returned = pool.Return(connection2);

        // then
        returned.Should().BeTrue();
    }

    [Fact]
    public void should_support_custom_nats_options()
    {
        // given
        var natsOpts = ConnectionFactory.GetDefaultOptions();
        natsOpts.Timeout = 10000;

        var options = MsOptions.Options.Create(
            new MessagingNatsOptions
            {
                Servers = "nats://custom-server:4222",
                ConnectionPoolSize = 5,
                Options = natsOpts,
            }
        );

        // when
        using var pool = new NatsConnectionPool(_logger, options);

        // then
        pool.ServersAddress.Should().Be("nats://custom-server:4222");
    }

    [Fact]
    public void should_log_configuration_on_creation()
    {
        // given
        var mockLogger = Substitute.For<ILogger<NatsConnectionPool>>();

        // when
        using var pool = new NatsConnectionPool(mockLogger, _options);

        // then
        mockLogger
            .Received()
            .Log(
                LogLevel.Debug,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }
}
