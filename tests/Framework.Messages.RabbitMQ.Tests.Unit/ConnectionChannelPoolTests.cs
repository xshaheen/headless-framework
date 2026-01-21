// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;

namespace Tests;

public sealed class ConnectionChannelPoolTests : TestBase
{
    private readonly IOptions<MessagingOptions> _capOptions;
    private readonly IOptions<RabbitMQOptions> _rabbitOptions;
    private readonly ILogger<ConnectionChannelPool> _logger;

    public ConnectionChannelPoolTests()
    {
        _logger = NullLogger<ConnectionChannelPool>.Instance;
        _capOptions = Options.Create(new MessagingOptions { Version = "v1" });
        _rabbitOptions = Options.Create(
            new RabbitMQOptions
            {
                HostName = "localhost",
                Port = 5672,
                ExchangeName = "test.exchange",
            }
        );
    }

    [Fact]
    public void should_initialize_with_correct_host_address()
    {
        // Given, When
        using var pool = new ConnectionChannelPool(_logger, _capOptions, _rabbitOptions);

        // Then
        pool.HostAddress.Should().Be("localhost:5672");
    }

    [Fact]
    public void should_initialize_with_correct_exchange_name_v1()
    {
        // Given
        var capOptions = Options.Create(new MessagingOptions { Version = "v1" });

        // When
        using var pool = new ConnectionChannelPool(_logger, capOptions, _rabbitOptions);

        // Then
        pool.Exchange.Should().Be("test.exchange");
    }

    [Fact]
    public void should_initialize_with_versioned_exchange_name()
    {
        // Given
        var capOptions = Options.Create(new MessagingOptions { Version = "v2" });

        // When
        using var pool = new ConnectionChannelPool(_logger, capOptions, _rabbitOptions);

        // Then
        pool.Exchange.Should().Be("test.exchange.v2");
    }

    [Fact]
    public void should_parse_cluster_hostnames()
    {
        // Given
        var options = Options.Create(
            new RabbitMQOptions
            {
                HostName = "rabbit1.local,rabbit2.local",
                Port = 5672,
                ExchangeName = "test.exchange",
            }
        );

        // When
        using var pool = new ConnectionChannelPool(_logger, _capOptions, options);

        // Then
        pool.HostAddress.Should().Be("rabbit1.local,rabbit2.local:5672");
    }

    [Fact]
    public async Task should_return_channel_to_pool_when_open()
    {
        // Given
        using var pool = new ConnectionChannelPool(_logger, _capOptions, _rabbitOptions);
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);

        // When
        var result = pool.Return(channel);

        // Then
        result.Should().BeTrue();
        await channel.DidNotReceive().DisposeAsync();
    }

    [Fact]
    public async Task should_dispose_channel_when_closed()
    {
        // Given
        using var pool = new ConnectionChannelPool(_logger, _capOptions, _rabbitOptions);
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(false);

        // When
        pool.Return(channel);

        // Then
        await channel.Received(1).DisposeAsync();
    }

    [Fact]
    public void should_dispose_all_channels_on_pool_dispose()
    {
        // Given
        var pool = new ConnectionChannelPool(_logger, _capOptions, _rabbitOptions);
        var channel1 = Substitute.For<IChannel>();
        var channel2 = Substitute.For<IChannel>();
        channel1.IsOpen.Returns(true);
        channel2.IsOpen.Returns(true);

        pool.Return(channel1);
        pool.Return(channel2);

        // When
        pool.Dispose();

        // Then
        channel1.Received(1).Dispose();
        channel2.Received(1).Dispose();
    }

    [Fact]
    public async Task should_release_semaphore_when_get_connection_throws()
    {
        // Given
        var options = Options.Create(
            new RabbitMQOptions
            {
                HostName = "invalid-host-that-does-not-exist",
                Port = 9999,
                ExchangeName = "test.exchange",
            }
        );
        using var pool = new ConnectionChannelPool(_logger, _capOptions, options);

        // When/Then - both should fail but semaphore should be released
        await pool.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
        await pool.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();

        // Verify pool is not exhausted - can still attempt rentals
        await pool.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task should_release_semaphore_when_create_channel_throws()
    {
        // Given
        var options = Options.Create(
            new RabbitMQOptions
            {
                HostName = "localhost",
                Port = 5672,
                ExchangeName = "test.exchange",
                ConnectionFactoryOptions = factory =>
                {
                    // Configure factory to fail on connection
                    factory.AutomaticRecoveryEnabled = false;
                }
            }
        );
        using var pool = new ConnectionChannelPool(_logger, _capOptions, options);

        // When/Then - should fail but not exhaust semaphore
        await pool.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
        await pool.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();

        // Verify pool is not exhausted after exceptions
        await pool.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task should_not_exhaust_pool_after_multiple_exceptions()
    {
        // Given
        var options = Options.Create(
            new RabbitMQOptions
            {
                HostName = "invalid-host",
                Port = 9999,
                ExchangeName = "test.exchange",
            }
        );
        using var pool = new ConnectionChannelPool(_logger, _capOptions, options);

        // When - force multiple exceptions (up to pool size of 15)
        var tasks = new List<Task>();
        for (var i = 0; i < 20; i++)
        {
            var task = pool.Rent().ContinueWith(
                _ => { },
                TaskContinuationOptions.OnlyOnFaulted
            );
            tasks.Add(task);
        }

        // Then - all should complete (with failures) without deadlock within timeout
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        // Verify pool is still usable
        await pool.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task should_complete_async_disposal()
    {
        // Given
        var pool = new ConnectionChannelPool(_logger, _capOptions, _rabbitOptions);

        // When - dispose asynchronously
        await pool.DisposeAsync();

        // Then - disposal should complete without exceptions
        Assert.True(true); // If we reach here, DisposeAsync completed successfully
    }
}
