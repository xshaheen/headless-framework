// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="RedisConsumerClientFactory"/>.
/// </summary>
public sealed class RedisConsumerClientFactoryTests : TestBase
{
    [Fact]
    public async Task should_preserve_factory_cancellation()
    {
        var factory = new RedisConsumerClientFactory(
            Options.Create(new RedisMessagingOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }),
            Options.Create(new MessagingOptions()),
            Substitute.For<IRedisStreamManager>(),
            LoggerFactory.CreateLogger<RedisConsumerClient>()
        );
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await factory.CreateAsync("test-group", 1, MessageLane.Queue, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_create_consumer_client_with_specified_group()
    {
        // given
        var mockStreamManager = Substitute.For<IRedisStreamManager>();
        var options = Options.Create(
            new RedisMessagingOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
        );
        var messagingOptions = Options.Create(new MessagingOptions());
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();

        var factory = new RedisConsumerClientFactory(options, messagingOptions, mockStreamManager, logger);

        // when
        var client = await factory.CreateAsync("my-consumer-group", 5, MessageLane.Queue, AbortToken);

        // then
        client.Should().NotBeNull();
        client.Should().BeOfType<RedisConsumerClient>();
        client.BrokerAddress.Name.Should().Be("redis");
    }

    [Fact]
    public async Task should_create_client_with_zero_concurrency()
    {
        // given
        var mockStreamManager = Substitute.For<IRedisStreamManager>();
        var options = Options.Create(
            new RedisMessagingOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
        );
        var messagingOptions = Options.Create(new MessagingOptions());
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();

        var factory = new RedisConsumerClientFactory(options, messagingOptions, mockStreamManager, logger);

        // when
        var client = await factory.CreateAsync("group-name", 0, MessageLane.Queue, AbortToken);

        // then
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_create_multiple_independent_clients()
    {
        // given
        var mockStreamManager = Substitute.For<IRedisStreamManager>();
        var options = Options.Create(
            new RedisMessagingOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
        );
        var messagingOptions = Options.Create(new MessagingOptions());
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();

        var factory = new RedisConsumerClientFactory(options, messagingOptions, mockStreamManager, logger);

        // when
        var client1 = await factory.CreateAsync("group-1", 1, MessageLane.Queue, AbortToken);
        var client2 = await factory.CreateAsync("group-2", 2, MessageLane.Queue, AbortToken);

        // then
        client1.Should().NotBeSameAs(client2);
    }

    [Fact]
    public async Task should_reject_bus_consumer_lane()
    {
        // given
        var mockStreamManager = Substitute.For<IRedisStreamManager>();
        var options = Options.Create(
            new RedisMessagingOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
        );
        var messagingOptions = Options.Create(new MessagingOptions());
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();

        var factory = new RedisConsumerClientFactory(options, messagingOptions, mockStreamManager, logger);

        // when
        var act = async () => await factory.CreateAsync("group-name", 1, MessageLane.Bus);

        // then
        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
