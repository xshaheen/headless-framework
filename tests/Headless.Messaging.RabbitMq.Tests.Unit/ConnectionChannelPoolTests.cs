// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Configuration;
using Headless.Messaging.RabbitMq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;

namespace Tests;

public sealed class ConnectionChannelPoolTests : TestBase
{
    private readonly IOptions<MessagingOptions> _capOptions = Options.Create(new MessagingOptions { Version = "v1" });
    private readonly IOptions<RabbitMqOptions> _rabbitOptions = Options.Create(
        new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            ExchangeName = "test.exchange",
        }
    );
    private readonly ILogger<ConnectionChannelPool> _logger = NullLogger<ConnectionChannelPool>.Instance;

    [Fact]
    public void should_initialize_with_correct_host_address()
    {
        // given, When
        using var pool = new ConnectionChannelPool(_logger, _capOptions, _rabbitOptions);

        // then
        pool.HostAddress.Should().Be("localhost:5672");
    }

    [Fact]
    public void should_initialize_with_correct_exchange_name_v1()
    {
        // given
        var capOptions = Options.Create(new MessagingOptions { Version = "v1" });

        // when
        using var pool = new ConnectionChannelPool(_logger, capOptions, _rabbitOptions);

        // then
        pool.Exchange.Should().Be("test.exchange");
    }

    [Fact]
    public void should_initialize_with_versioned_exchange_name()
    {
        // given
        var capOptions = Options.Create(new MessagingOptions { Version = "v2" });

        // when
        using var pool = new ConnectionChannelPool(_logger, capOptions, _rabbitOptions);

        // then
        pool.Exchange.Should().Be("test.exchange.v2");
    }

    [Fact]
    public void should_parse_cluster_hostnames()
    {
        // given
        var options = Options.Create(
            new RabbitMqOptions
            {
                HostName = "rabbit1.local,rabbit2.local",
                Port = 5672,
                ExchangeName = "test.exchange",
            }
        );

        // when
        using var pool = new ConnectionChannelPool(_logger, _capOptions, options);

        // then
        pool.HostAddress.Should().Be("rabbit1.local,rabbit2.local:5672");
    }

    [Fact]
    public async Task should_return_channel_to_pool_when_open()
    {
        // given
        await using var pool = new ConnectionChannelPool(_logger, _capOptions, _rabbitOptions);
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);

        // when
        var result = pool.Return(channel);

        // then
        result.Should().BeTrue();
        await channel.DidNotReceive().DisposeAsync();
    }

    [Fact]
    public async Task should_dispose_channel_when_closed()
    {
        // given
        await using var pool = new ConnectionChannelPool(_logger, _capOptions, _rabbitOptions);
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(false);

        // when
        pool.Return(channel);

        // then
        await channel.Received(1).DisposeAsync();
    }

    [Fact]
    public void should_dispose_all_channels_on_pool_dispose()
    {
        // given
        var pool = new ConnectionChannelPool(_logger, _capOptions, _rabbitOptions);
        var channel1 = Substitute.For<IChannel>();
        var channel2 = Substitute.For<IChannel>();
        channel1.IsOpen.Returns(true);
        channel2.IsOpen.Returns(true);

        pool.Return(channel1);
        pool.Return(channel2);

        // when
        pool.Dispose();

        // then
        channel1.Received(1).Dispose();
        channel2.Received(1).Dispose();
    }

    [Fact]
    public async Task should_release_semaphore_when_get_connection_throws()
    {
        // given
        var options = Options.Create(
            new RabbitMqOptions
            {
                HostName = "invalid-host-that-does-not-exist",
                Port = 9999,
                ExchangeName = "test.exchange",
            }
        );

        await using var pool = new ConnectionChannelPool(_logger, _capOptions, options);

        // when/Then - both should fail but semaphore should be released
        await pool.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
        await pool.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();

        // Verify pool is not exhausted - can still attempt rentals
        await pool.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task should_release_semaphore_when_create_channel_throws()
    {
        // given
        var options = Options.Create(
            new RabbitMqOptions
            {
                HostName = "localhost",
                Port = 5672,
                ExchangeName = "test.exchange",
                ConnectionFactoryOptions = factory =>
                {
                    // Configure factory to fail on connection
                    factory.AutomaticRecoveryEnabled = false;
                },
            }
        );

        await using var pool = new ConnectionChannelPool(_logger, _capOptions, options);

        // when/Then - should fail but not exhaust semaphore
        await pool.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
        await pool.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();

        // Verify pool is not exhausted after exceptions
        await pool.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task should_not_exhaust_pool_after_multiple_exceptions()
    {
        // given
        var options = Options.Create(
            new RabbitMqOptions
            {
                HostName = "invalid-host",
                Port = 9999,
                ExchangeName = "test.exchange",
            }
        );

        await using var pool = new ConnectionChannelPool(_logger, _capOptions, options);

        // when - force multiple exceptions (up to pool size of 15)
        var rentTasks = Enumerable
            .Range(0, 20)
            .Select(async _ =>
            {
                try
                {
                    await pool.Rent();
                }
#pragma warning disable ERP022
                catch
                {
                    // expected: all calls should fail due to invalid host
                }
#pragma warning restore ERP022
            })
            .ToList();

        // then - all should complete (with failures) without deadlock within timeout
        await Task.WhenAll(rentTasks).WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // Verify pool is still usable
        await pool.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task should_complete_async_disposal()
    {
        // given
        var pool = new ConnectionChannelPool(_logger, _capOptions, _rabbitOptions);

        // when & then - dispose asynchronously, then verify no exceptions
        await pool.DisposeAsync();
    }

    [Fact]
    public async Task should_dispose_channels_on_async_disposal()
    {
        // given
        await using var pool = new ConnectionChannelPool(_logger, _capOptions, _rabbitOptions);
        var channel1 = Substitute.For<IChannel>();
        var channel2 = Substitute.For<IChannel>();
        channel1.IsOpen.Returns(true);
        channel2.IsOpen.Returns(true);

        pool.Return(channel1);
        pool.Return(channel2);

        // when
        await pool.DisposeAsync();

        // then
        await channel1.Received(1).DisposeAsync();
        await channel2.Received(1).DisposeAsync();
    }

    [Fact]
    public void should_not_return_channel_to_pool_when_max_size_exceeded()
    {
        // given
        var pool = new ConnectionChannelPool(_logger, _capOptions, _rabbitOptions);

        // Dispose sets _maxSize to 0
        pool.Dispose();

        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);

        // when
        var result = pool.Return(channel);

        // then
        result.Should().BeFalse();
        channel.Received(1).Dispose();
    }

    [Fact]
    public void should_not_enqueue_closed_channel_on_return()
    {
        // given
        using var pool = new ConnectionChannelPool(_logger, _capOptions, _rabbitOptions);
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(false);

        // when
        var result = pool.Return(channel);

        // then - closed channel should not be returned to pool
        result.Should().BeFalse();
    }

    [Fact]
    public void should_expose_correct_exchange_name()
    {
        // given
        var options = Options.Create(
            new RabbitMqOptions
            {
                HostName = "localhost",
                Port = 5672,
                ExchangeName = "custom.exchange",
            }
        );

        // when
        using var pool = new ConnectionChannelPool(_logger, _capOptions, options);

        // then
        pool.Exchange.Should().Be("custom.exchange");
    }
}
