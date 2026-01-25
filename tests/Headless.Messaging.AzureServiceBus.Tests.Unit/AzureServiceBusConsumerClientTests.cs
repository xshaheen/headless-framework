// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.AzureServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class AzureServiceBusConsumerClientTests
{
    private readonly ILogger _logger;
    private readonly IOptions<AzureServiceBusOptions> _options;
    private readonly IServiceProvider _serviceProvider;

    public AzureServiceBusConsumerClientTests()
    {
        _logger = Substitute.For<ILogger>();
        _options = Options.Create(
            new AzureServiceBusOptions
            {
                ConnectionString =
                    "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey",
            }
        );
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
    }

    [Fact]
    public void should_throw_when_options_value_is_null()
    {
        // given
        var nullOptions = Options.Create<AzureServiceBusOptions>(null!);

        // when
        var act = () => new AzureServiceBusConsumerClient(_logger, "test-sub", 1, nullOptions, _serviceProvider);

        // then
        act.Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public async Task should_have_correct_broker_address_from_connection_string()
    {
        // given, when
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);

        // then
        client.BrokerAddress.Name.Should().Be("servicebus");
        client.BrokerAddress.Endpoint.Should().Be("sb://mynamespace.servicebus.windows.net/");
    }

    [Fact]
    public async Task should_have_correct_broker_address_from_namespace()
    {
        // given
        var options = Options.Create(
            new AzureServiceBusOptions { Namespace = "sb://custom.servicebus.windows.net/" }
        );

        // when
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, options, _serviceProvider);

        // then
        client.BrokerAddress.Endpoint.Should().Be("sb://custom.servicebus.windows.net/");
    }

    [Fact]
    public async Task should_initialize_callbacks_as_null()
    {
        // given, when
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);

        // then
        client.OnMessageCallback.Should().BeNull();
        client.OnLogCallback.Should().BeNull();
    }

    [Fact]
    public async Task should_allow_setting_on_message_callback()
    {
        // given
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);
        Func<Headless.Messaging.Messages.TransportMessage, object?, Task> callback = (_, _) => Task.CompletedTask;

        // when
        client.OnMessageCallback = callback;

        // then
        client.OnMessageCallback.Should().BeSameAs(callback);
    }

    [Fact]
    public async Task should_allow_setting_on_log_callback()
    {
        // given
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);
        Action<Headless.Messaging.Transport.LogMessageEventArgs> callback = _ => { };

        // when
        client.OnLogCallback = callback;

        // then
        client.OnLogCallback.Should().BeSameAs(callback);
    }

    [Fact]
    public async Task should_throw_when_subscribing_with_null_topics()
    {
        // given
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);

        // when
        var act = async () => await client.SubscribeAsync(null!);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_throw_when_connecting_without_connection_info()
    {
        // given
        var options = Options.Create(
            new AzureServiceBusOptions
            {
                // Missing both connection string and namespace
                ConnectionString = null!,
                Namespace = null!,
            }
        );
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, options, _serviceProvider);

        // when
        var act = async () => await client.ConnectAsync();

        // then - Azure SDK throws ArgumentNullException for missing connection string
        await act.Should().ThrowAsync<ArgumentNullException>().WithMessage("*connectionString*");
    }

    [Fact]
    public async Task should_dispose_without_error_when_not_connected()
    {
        // given
        var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);

        // when
        var act = async () => await client.DisposeAsync();

        // then
        await act.Should().NotThrowAsync();
    }
}
