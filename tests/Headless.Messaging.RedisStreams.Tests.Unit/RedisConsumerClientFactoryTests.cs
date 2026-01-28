// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.RedisStreams;
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
    public async Task should_create_consumer_client_with_specified_group()
    {
        // given
        var mockStreamManager = Substitute.For<IRedisStreamManager>();
        var options = Options.Create(
            new MessagingRedisOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
        );
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();

        var factory = new RedisConsumerClientFactory(options, mockStreamManager, logger);

        // when
        var client = await factory.CreateAsync("my-consumer-group", 5);

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
            new MessagingRedisOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
        );
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();

        var factory = new RedisConsumerClientFactory(options, mockStreamManager, logger);

        // when
        var client = await factory.CreateAsync("group-name", 0);

        // then
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_create_multiple_independent_clients()
    {
        // given
        var mockStreamManager = Substitute.For<IRedisStreamManager>();
        var options = Options.Create(
            new MessagingRedisOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
        );
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();

        var factory = new RedisConsumerClientFactory(options, mockStreamManager, logger);

        // when
        var client1 = await factory.CreateAsync("group-1", 1);
        var client2 = await factory.CreateAsync("group-2", 2);

        // then
        client1.Should().NotBeSameAs(client2);
    }
}
