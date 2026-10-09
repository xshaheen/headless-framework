// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
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
            Substitute.For<IRedisStreamManager>(),
            LoggerFactory.CreateLogger<RedisConsumerClient>()
        );
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () =>
            await factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue), cts.Token);

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
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();

        var factory = new RedisConsumerClientFactory(options, mockStreamManager, logger);

        // when
        var client = await factory.CreateAsync(
            new ConsumerClientRequest("my-consumer-group", 5, MessageLane.Queue),
            AbortToken
        );

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
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();

        var factory = new RedisConsumerClientFactory(options, mockStreamManager, logger);

        // when
        var client = await factory.CreateAsync(
            new ConsumerClientRequest("group-name", 0, MessageLane.Queue),
            AbortToken
        );

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
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();

        var factory = new RedisConsumerClientFactory(options, mockStreamManager, logger);

        // when
        var client1 = await factory.CreateAsync(new ConsumerClientRequest("group-1", 1, MessageLane.Queue), AbortToken);
        var client2 = await factory.CreateAsync(new ConsumerClientRequest("group-2", 2, MessageLane.Queue), AbortToken);

        // then
        client1.Should().NotBeSameAs(client2);
    }

    [Fact]
    public async Task should_create_bus_consumer_lane()
    {
        // given
        var mockStreamManager = Substitute.For<IRedisStreamManager>();
        var options = Options.Create(
            new RedisMessagingOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
        );
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();

        var factory = new RedisConsumerClientFactory(options, mockStreamManager, logger);

        // when
        var client = await factory.CreateAsync(new ConsumerClientRequest("group-name", 1, MessageLane.Bus), AbortToken);

        // then
        client.Should().BeOfType<RedisConsumerClient>();
        await client.DisposeAsync();
    }

    [Fact]
    public async Task should_give_each_live_client_of_one_group_its_own_stable_consumer_name()
    {
        // given
        var mockStreamManager = Substitute.For<IRedisStreamManager>();
        var options = Options.Create(
            new RedisMessagingOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
        );
        var factory = new RedisConsumerClientFactory(
            options,
            mockStreamManager,
            LoggerFactory.CreateLogger<RedisConsumerClient>()
        );
        var request = new ConsumerClientRequest("orders", 1, MessageLane.Queue);

        // when
        var first = await factory.CreateAsync(request, AbortToken);
        var second = await factory.CreateAsync(request, AbortToken);
        await _ListenOnceAsync(first);
        await _ListenOnceAsync(second);
        await first.DisposeAsync();
        var replacement = await factory.CreateAsync(request, AbortToken);
        await _ListenOnceAsync(replacement);

        // then
        var names = mockStreamManager
            .ReceivedCalls()
            .Where(call =>
                string.Equals(
                    call.GetMethodInfo().Name,
                    nameof(IRedisStreamManager.PollStreamsPendingMessagesAsync),
                    StringComparison.Ordinal
                )
            )
            .Select(call => (string)call.GetArguments()[2]!)
            .ToList();
        var prefix = $"orders:{Environment.MachineName}";
        names.Should().Equal($"{prefix}:0", $"{prefix}:1", $"{prefix}:0");

        await second.DisposeAsync();
        await replacement.DisposeAsync();
    }

    private async Task _ListenOnceAsync(IConsumerClient client)
    {
        await client.SubscribeAsync(["orders"], AbortToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token);
        await cts.CancelAsync();
        await listening;
    }
}
