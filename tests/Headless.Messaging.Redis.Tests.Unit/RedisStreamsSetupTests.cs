// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="SetupRedisMessaging"/>.
/// </summary>
public sealed class RedisStreamsSetupTests : TestBase
{
    [Fact]
    public async Task should_register_redis_services_with_connection_string()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(opt => opt.UseRedis("localhost:6379"));

        // when
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetService<IQueueTransport>().Should().BeOfType<RedisTransport>();
        provider.GetService<IConsumerClientFactory>().Should().BeOfType<RedisConsumerClientFactorySelector>();
        provider.GetService<IRedisStreamManager>().Should().NotBeNull();
        provider.GetService<IRedisConnectionPool>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_register_redis_services_with_configure_action()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(opt =>
            opt.UseRedis(redisOpt =>
            {
                redisOpt.Configuration = ConfigurationOptions.Parse("redis.example.com:6380");
                redisOpt.ConnectionPoolSize = 15;
                redisOpt.StreamEntriesCount = 50;
            })
        );

        // when
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RedisMessagingOptions>>().Value;

        // then
        options.ConnectionPoolSize.Should().Be(15);
        options.StreamEntriesCount.Should().Be(50);
    }

    [Fact]
    public async Task should_apply_default_values_via_post_configure()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(opt => opt.UseRedis(_ => { }));

        // when
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RedisMessagingOptions>>().Value;

        // then - defaults should be applied
        options.StreamEntriesCount.Should().Be(10);
        options.ConnectionPoolSize.Should().Be(10);
        options.Configuration.Should().NotBeNull();
        options.Configuration!.EndPoints.Should().NotBeEmpty();
    }

    [Fact]
    public async Task should_register_message_queue_marker_service()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(opt => opt.UseRedis());

        // when
        await using var provider = services.BuildServiceProvider();
        var marker = provider.GetService<MessageQueueMarkerService>();

        // then
        marker.Should().NotBeNull();
        marker!.Name.Should().Be("Redis");
    }

    [Fact]
    public async Task should_route_consumer_clients_by_intent_when_streams_and_pubsub_are_configured()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(opt =>
        {
            opt.UseRedis();
            opt.UseRedisPubSub();
        });

        // when
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConsumerClientFactory>();

        // then
        var intentAwareFactory = factory.Should().BeAssignableTo<IIntentAwareConsumerClientFactory>().Subject;
        await using var queueClient = await intentAwareFactory.CreateAsync("queue-group", 1, IntentType.Queue);
        await using var busClient = await intentAwareFactory.CreateAsync("bus-group", 1, IntentType.Bus);

        queueClient.Should().BeOfType<RedisConsumerClient>();
        busClient.Should().BeOfType<RedisPubSubConsumerClient>();
    }

    [Theory]
    [InlineData(IntentType.Bus)]
    [InlineData(IntentType.Queue)]
    public async Task should_propagate_factory_cancellation_through_intent_selector(IntentType intentType)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(opt =>
        {
            opt.UseRedis();
            opt.UseRedisPubSub();
        });
        await using var provider = services.BuildServiceProvider();
        var factory = provider
            .GetRequiredService<IConsumerClientFactory>()
            .Should()
            .BeAssignableTo<IIntentAwareConsumerClientFactory>()
            .Subject;
        var cancellationToken = new CancellationToken(canceled: true);

        var act = async () => await factory.CreateAsync("test-group", 1, intentType, cancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void should_throw_when_configure_action_is_null()
    {
        // given
        var services = new ServiceCollection();
        Action<RedisMessagingOptions>? nullAction = null;

        // when & then
        var action = () => services.AddHeadlessMessaging(opt => opt.UseRedis(nullAction!));
        action.Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public async Task should_preserve_explicitly_set_options()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(opt =>
            opt.UseRedis(redisOpt =>
            {
                redisOpt.Configuration = ConfigurationOptions.Parse("custom-host:1234");
                redisOpt.ConnectionPoolSize = 25;
                redisOpt.StreamEntriesCount = 100;
            })
        );

        // when
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RedisMessagingOptions>>().Value;

        // then - explicit values should not be overwritten by defaults
        options.ConnectionPoolSize.Should().Be(25);
        options.StreamEntriesCount.Should().Be(100);
        options.Configuration!.EndPoints.Should().Contain(e => e.ToString()!.Contains("custom-host:1234"));
    }
}
