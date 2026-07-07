// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class AzureServiceBusConsumerClientFactoryTests
{
    [Fact]
    public async Task should_throw_broker_connection_exception_when_options_are_invalid()
    {
        // given
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

        var options = Options.Create(
            new AzureServiceBusMessagingOptions
            {
                // Invalid/missing connection info will cause connection failure
                ConnectionString = null!,
                Namespace = null!,
            }
        );
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var factory = new AzureServiceBusConsumerClientFactory(loggerFactory, options, serviceProvider);

        // when
        var act = async () => await factory.CreateAsync("test-group", 5);

        // then
        await act.Should().ThrowAsync<BrokerConnectionException>();
    }

    [Fact]
    public async Task should_throw_broker_connection_exception_when_connection_string_is_malformed()
    {
        // given
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

        var options = Options.Create(
            new AzureServiceBusMessagingOptions
            {
                // Malformed connection string
                ConnectionString = "InvalidConnectionString",
            }
        );
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var factory = new AzureServiceBusConsumerClientFactory(loggerFactory, options, serviceProvider);

        // when
        var act = async () => await factory.CreateAsync("test-group", 5);

        // then
        await act.Should().ThrowAsync<BrokerConnectionException>();
    }

    [Fact]
    public async Task should_allow_queue_consumer_group_longer_than_subscription_limit()
    {
        // given
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

        var options = Options.Create(
            new AzureServiceBusMessagingOptions { ConnectionString = "InvalidConnectionString" }
        );
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var factory = new AzureServiceBusConsumerClientFactory(loggerFactory, options, serviceProvider);
        var groupName = new string('a', 80);

        // when
        var act = async () => await factory.CreateAsync(groupName, 5, IntentType.Queue);

        // then - reaches connection setup instead of rejecting the framework-local group name.
        await act.Should().ThrowAsync<BrokerConnectionException>();
    }
}
