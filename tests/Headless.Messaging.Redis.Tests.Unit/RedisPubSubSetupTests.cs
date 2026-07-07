// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisPubSubSetupTests : TestBase
{
    [Fact]
    public async Task should_register_redis_pubsub_as_bus_only_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.UseRedisPubSub("localhost:6379"));

        // when
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetService<IBusTransport>().Should().BeOfType<RedisPubSubBusTransport>();
        provider.GetService<IQueueTransport>().Should().BeNull();
        provider.GetService<IConsumerClientFactory>().Should().BeOfType<RedisConsumerClientFactorySelector>();
        provider.GetService<IRedisPubSubConnectionProvider>().Should().BeOfType<RedisPubSubConnectionProvider>();
    }

    [Fact]
    public async Task should_apply_default_connection_options()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.UseRedisPubSub());

        // when
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RedisPubSubMessagingOptions>>().Value;

        // then
        options.Configuration.Should().NotBeNull();
        options.Configuration!.EndPoints.Should().NotBeEmpty();
    }

    [Fact]
    public async Task should_preserve_explicit_connection_options()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
            setup.UseRedisPubSub(options =>
                options.Configuration = ConfigurationOptions.Parse("redis.example.com:6380")
            )
        );

        // when
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RedisPubSubMessagingOptions>>().Value;

        // then
        options.Configuration!.EndPoints.Should().Contain(e => e.ToString()!.Contains("redis.example.com:6380"));
    }

    [Fact]
    public void should_throw_when_configure_action_is_null()
    {
        // given
        var services = new ServiceCollection();
        Action<RedisPubSubMessagingOptions>? configure = null;

        // when
        var action = () => services.AddHeadlessMessaging(setup => setup.UseRedisPubSub(configure!));

        // then
        action.Should().ThrowExactly<ArgumentNullException>();
    }
}
