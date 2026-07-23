// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.RabbitMq;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Tests;

public sealed class ConnectionChannelPoolTests : TestBase
{
    private readonly IOptions<MessagingOptions> _capOptions = Options.Create(new MessagingOptions { Version = "v1" });
    private readonly IOptions<RabbitMqMessagingOptions> _rabbitOptions = Options.Create(
        new RabbitMqMessagingOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "test_user",
            Password = "test_pass",
            ExchangeName = "test.exchange",
        }
    );
    private readonly ILogger<ConnectionChannelPool> _logger = NullLogger<ConnectionChannelPool>.Instance;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_configure_confirmation_tracking_to_match_publish_confirms(bool publishConfirms)
    {
        var options = ConnectionChannelPool.BuildChannelOptions(publishConfirms);

        options.PublisherConfirmationsEnabled.Should().Be(publishConfirms);
        options.PublisherConfirmationTrackingEnabled.Should().Be(publishConfirms);
    }

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
            new RabbitMqMessagingOptions
            {
                HostName = "rabbit1.local,rabbit2.local",
                Port = 5672,
                UserName = "test_user",
                Password = "test_pass",
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

        // then - Return() uses sync Dispose(), not DisposeAsync()
        channel.Received(1).Dispose();
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
            new RabbitMqMessagingOptions
            {
                HostName = "invalid-host-that-does-not-exist",
                Port = 9999,
                UserName = "test_user",
                Password = "test_pass",
                ExchangeName = "test.exchange",
            }
        );

        await using var pool = new ConnectionChannelPool(_logger, _capOptions, options);
        var sut = (IConnectionChannelPool)pool;

        // when/Then - both should fail but semaphore should be released
        await sut.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
        await sut.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();

        // Verify pool is not exhausted - can still attempt rentals
        await sut.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task should_release_semaphore_when_create_channel_throws()
    {
        // given
        var options = Options.Create(
            new RabbitMqMessagingOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "test_user",
                Password = "test_pass",
                ExchangeName = "test.exchange",
                ConnectionFactoryOptions = factory =>
                {
                    // Configure factory to fail on connection
                    factory.AutomaticRecoveryEnabled = false;
                },
            }
        );

        await using var pool = new ConnectionChannelPool(_logger, _capOptions, options);
        var sut = (IConnectionChannelPool)pool;

        // when/Then - should fail but not exhaust semaphore
        await sut.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
        await sut.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();

        // Verify pool is not exhausted after exceptions
        await sut.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task should_not_exhaust_pool_after_multiple_exceptions()
    {
        // given - use a loopback port that nothing listens on so the connect fails with
        // ECONNREFUSED in microseconds. The previous host "invalid-host" went through DNS,
        // which under load (search-domain timeouts) easily exceeded the 5s budget because
        // ConnectionChannelPool serializes the underlying connect via _connectionLock.
        var options = Options.Create(
            new RabbitMqMessagingOptions
            {
                HostName = "127.0.0.1",
                Port = 1,
                UserName = "test_user",
                Password = "test_pass",
                ExchangeName = "test.exchange",
            }
        );

        await using var pool = new ConnectionChannelPool(_logger, _capOptions, options);
        var sut = (IConnectionChannelPool)pool;

        // when - force multiple exceptions (up to pool size of 15)
        var rentTasks = Enumerable.Range(0, 20).Select(_ => sut.Rent(AbortToken)).ToList();
        var allRentTasks = Task.WhenAll(rentTasks);

        // then - all should complete (with failures) without deadlock within timeout
        var completedTask = await Task.WhenAny(allRentTasks, Task.Delay(TimeSpan.FromSeconds(10), AbortToken));
        completedTask.Should().Be(allRentTasks);
        await allRentTasks.Invoking(static task => task).Should().ThrowAsync<Exception>();

        // Verify pool is still usable
        await sut.Invoking(p => p.Rent()).Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task should_not_over_release_semaphore_when_rented_and_returned_through_interface()
    {
        // given - a channel pre-seeded into the pool so interface Rent() can hand it out without a
        // real broker. The interface Rent acquires _poolSemaphore; the interface Return releases it.
        // Each rent/return cycle must be balanced.
        await using var pool = new ConnectionChannelPool(_logger, _capOptions, _rabbitOptions);
        var sut = (IConnectionChannelPool)pool;
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);

        // seed the pool so Rent() dequeues this channel instead of opening a connection
        pool.Return(channel);

        // when - cycle Rent+Return more times than the pool size (_DefaultPoolSize = 15).
        // Pre-fix, Return released the semaphore without Rent acquiring it, so after 15 returns the
        // semaphore over-released and threw SemaphoreFullException.
        var act = async () =>
        {
            for (var i = 0; i < 30; i++)
            {
                var rented = await sut.Rent(AbortToken);
                sut.Return(rented);
            }
        };

        // then - no over-release, and the pool stays usable for one more cycle
        await act.Should().NotThrowAsync();

        var last = await sut.Rent(AbortToken);
        sut.Return(last).Should().BeTrue();
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
            new RabbitMqMessagingOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "test_user",
                Password = "test_pass",
                ExchangeName = "custom.exchange",
            }
        );

        // when
        using var pool = new ConnectionChannelPool(_logger, _capOptions, options);

        // then
        pool.Exchange.Should().Be("custom.exchange");
    }
}
