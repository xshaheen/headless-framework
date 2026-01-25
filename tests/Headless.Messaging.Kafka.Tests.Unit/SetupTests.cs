// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Configuration;
using Headless.Messaging.Kafka;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void UseKafka_with_bootstrap_servers_should_register_services()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddMessages(options =>
        {
            options.UseKafka("localhost:9092");
        });

        var provider = services.BuildServiceProvider();

        // then
        provider.GetService<ITransport>().Should().NotBeNull();
        provider.GetService<ITransport>().Should().BeOfType<KafkaTransport>();
        provider.GetService<IConsumerClientFactory>().Should().NotBeNull();
        provider.GetService<IConsumerClientFactory>().Should().BeOfType<KafkaConsumerClientFactory>();
        provider.GetService<IKafkaConnectionPool>().Should().NotBeNull();
        provider.GetService<IKafkaConnectionPool>().Should().BeOfType<KafkaConnectionPool>();
    }

    [Fact]
    public void UseKafka_with_configure_action_should_configure_options()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddMessages(options =>
        {
            options.UseKafka(opt =>
            {
                opt.Servers = "broker1:9092,broker2:9092";
                opt.ConnectionPoolSize = 20;
            });
        });

        var provider = services.BuildServiceProvider();
        var kafkaOptions = provider.GetRequiredService<IOptions<MessagingKafkaOptions>>().Value;

        // then
        kafkaOptions.Servers.Should().Be("broker1:9092,broker2:9092");
        kafkaOptions.ConnectionPoolSize.Should().Be(20);
    }

    [Fact]
    public void UseKafka_should_throw_when_configure_is_null()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddMessages(options =>
        {
            options.UseKafka((Action<MessagingKafkaOptions>)null!);
        });

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseKafka_should_register_MessageQueueMarkerService()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddMessages(options =>
        {
            options.UseKafka("localhost:9092");
        });

        var provider = services.BuildServiceProvider();
        var marker = provider.GetService<MessageQueueMarkerService>();

        // then
        marker.Should().NotBeNull();
        marker!.Name.Should().Be("Kafka");
    }

    [Fact]
    public void UseKafka_should_register_as_singletons()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessages(options =>
        {
            options.UseKafka("localhost:9092");
        });

        var provider = services.BuildServiceProvider();

        // when
        var pool1 = provider.GetService<IKafkaConnectionPool>();
        var pool2 = provider.GetService<IKafkaConnectionPool>();

        // then
        pool1.Should().BeSameAs(pool2);
    }
}
