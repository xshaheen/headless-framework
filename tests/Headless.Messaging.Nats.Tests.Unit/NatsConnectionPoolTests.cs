// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.Core;
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
            new MessagingNatsOptions { Servers = "nats://localhost:4222", ConnectionPoolSize = 3 }
        );
    }

    [Fact]
    public async Task should_initialize_with_correct_server_address()
    {
        await using var pool = new NatsConnectionPool(_logger, _options);
        pool.ServersAddress.Should().Be("nats://localhost:4222");
    }

    [Fact]
    public async Task should_redact_credentials_from_server_address()
    {
        var options = MsOptions.Options.Create(
            new MessagingNatsOptions { Servers = "nats://user:password@localhost:4222", ConnectionPoolSize = 1 }
        );

        await using var pool = new NatsConnectionPool(_logger, options);
        pool.ServersAddress.Should().Be("nats://localhost:4222");
    }

    [Fact]
    public async Task should_return_connection_via_round_robin()
    {
        await using var pool = new NatsConnectionPool(_logger, _options);

        var conn1 = pool.GetConnection();
        var conn2 = pool.GetConnection();
        var conn3 = pool.GetConnection();
        var conn4 = pool.GetConnection(); // wraps around

        conn1.Should().NotBeNull();
        conn2.Should().NotBeNull();
        conn3.Should().NotBeNull();
        conn4.Should().BeSameAs(conn1); // round-robin wraps
    }

    [Fact]
    public async Task should_create_pool_size_connections()
    {
        var options = MsOptions.Options.Create(
            new MessagingNatsOptions { Servers = "nats://localhost:4222", ConnectionPoolSize = 5 }
        );

        await using var pool = new NatsConnectionPool(_logger, options);

        var connections = Enumerable.Range(0, 5).Select(_ => pool.GetConnection()).ToList();
        connections.Distinct().Should().HaveCount(5);
    }

    [Fact]
    public async Task should_log_configuration_on_creation()
    {
        var mockLogger = Substitute.For<ILogger<NatsConnectionPool>>();
        mockLogger.IsEnabled(LogLevel.Debug).Returns(true);

        await using var pool = new NatsConnectionPool(mockLogger, _options);

        mockLogger.ReceivedCalls().Should().ContainSingle(call => _IsDebugLog(call));
    }

    [Fact]
    public async Task should_throw_object_disposed_when_getting_connection_after_dispose()
    {
        var pool = new NatsConnectionPool(_logger, _options);
        await pool.DisposeAsync();

        var act = () => pool.GetConnection();
        act.Should().Throw<ObjectDisposedException>();
    }

    private static bool _IsDebugLog(ICall call)
    {
        if (call.GetMethodInfo().Name != nameof(ILogger.Log))
            return false;

        return call.GetArguments()[0] is LogLevel.Debug;
    }
}
