// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Configuration;
using Headless.Messaging.RedisStreams;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="MessagesRedisSetup"/>.
/// </summary>
public sealed class SetupTests : TestBase
{
    [Fact]
    public void should_register_redis_services_with_connection_string()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(opt => opt.UseRedis("localhost:6379"));

        // when
        var provider = services.BuildServiceProvider();

        // then
        provider.GetService<ITransport>().Should().NotBeNull();
        provider.GetService<IConsumerClientFactory>().Should().NotBeNull();
        provider.GetService<IRedisStreamManager>().Should().NotBeNull();
        provider.GetService<IRedisConnectionPool>().Should().NotBeNull();
    }

    [Fact]
    public void should_register_redis_services_with_configure_action()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(opt =>
            opt.UseRedis(redisOpt =>
            {
                redisOpt.Configuration = ConfigurationOptions.Parse("redis.example.com:6380");
                redisOpt.ConnectionPoolSize = 15;
                redisOpt.StreamEntriesCount = 50;
            })
        );

        // when
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingRedisOptions>>().Value;

        // then
        options.ConnectionPoolSize.Should().Be(15);
        options.StreamEntriesCount.Should().Be(50);
    }

    [Fact]
    public void should_apply_default_values_via_post_configure()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(opt => opt.UseRedis(_ => { }));

        // when
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingRedisOptions>>().Value;

        // then - defaults should be applied
        options.StreamEntriesCount.Should().Be(10);
        options.ConnectionPoolSize.Should().Be(10);
        options.Configuration.Should().NotBeNull();
        options.Configuration!.EndPoints.Should().NotBeEmpty();
    }

    [Fact]
    public void should_register_message_queue_marker_service()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(opt => opt.UseRedis());

        // when
        var provider = services.BuildServiceProvider();
        var marker = provider.GetService<MessageQueueMarkerService>();

        // then
        marker.Should().NotBeNull();
        marker!.Name.Should().Be("RedisStreams");
    }

    [Fact]
    public void should_throw_when_configure_action_is_null()
    {
        // given
        var services = new ServiceCollection();
        Action<MessagingRedisOptions>? nullAction = null;

        // when & then
        var action = () => services.AddMessages(opt => opt.UseRedis(nullAction!));
        action.Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void should_preserve_explicitly_set_options()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(opt =>
            opt.UseRedis(redisOpt =>
            {
                redisOpt.Configuration = ConfigurationOptions.Parse("custom-host:1234");
                redisOpt.ConnectionPoolSize = 25;
                redisOpt.StreamEntriesCount = 100;
            })
        );

        // when
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingRedisOptions>>().Value;

        // then - explicit values should not be overwritten by defaults
        options.ConnectionPoolSize.Should().Be(25);
        options.StreamEntriesCount.Should().Be(100);
        options.Configuration!.EndPoints.Should().Contain(e => e.ToString()!.Contains("custom-host:1234"));
    }
}
