// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class AzureServiceBusConsumerClientTests : TestBase
{
    private readonly ILogger _logger = Substitute.For<ILogger>();
    private readonly IOptions<AzureServiceBusMessagingOptions> _options = Options.Create(
        new AzureServiceBusMessagingOptions
        {
            ConnectionString =
                "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey",
        }
    );
    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void should_throw_when_options_value_is_null()
    {
        // given
        var nullOptions = Options.Create<AzureServiceBusMessagingOptions>(null!);

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
            new AzureServiceBusMessagingOptions { Namespace = "sb://custom.servicebus.windows.net/" }
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
        Func<TransportMessage, object?, Task> callback = (_, _) => Task.CompletedTask;

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
        Action<LogMessageEventArgs> callback = _ => { };

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
    public void CheckValidQueueName_should_allow_names_longer_than_subscription_rule_limit()
    {
        var queueName = new string('a', 80);

        var act = () => AzureServiceBusConsumerClient.CheckValidQueueName(queueName);

        act.Should().NotThrow();
    }

    [Fact]
    public void CheckValidQueueName_should_reject_reserved_uri_characters()
    {
        var act = () => AzureServiceBusConsumerClient.CheckValidQueueName("orders#created");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_when_connecting_without_connection_info()
    {
        // given
        var options = Options.Create(
            new AzureServiceBusMessagingOptions
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
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);

        // when
        var act = async () => await client.DisposeAsync();

        // then
        await act.Should().NotThrowAsync();
    }

    // -------------------------------------------------------------------------
    // PauseAsync / ResumeAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PauseAsync_is_noop_when_processor_is_null()
    {
        // given — no ConnectAsync called, processor is null
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);

        // when
        await client.PauseAsync(AbortToken);

        // then — no exception
    }

    [Fact]
    public async Task PauseAsync_is_idempotent_when_called_twice()
    {
        // given
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);

        // when
        await client.PauseAsync(AbortToken);
        await client.PauseAsync(AbortToken);

        // then — no exception, second call is no-op
    }

    [Fact]
    public async Task ResumeAsync_is_noop_when_not_paused()
    {
        // given
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);

        // when
        await client.ResumeAsync(AbortToken);

        // then — no exception
    }

    [Fact]
    public async Task PauseAsync_then_ResumeAsync_completes_full_cycle()
    {
        // given
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);

        // when
        await client.PauseAsync(AbortToken);
        await client.ResumeAsync(AbortToken);

        // then — no exception
    }

    [Fact]
    public async Task ResumeAsync_is_idempotent_after_resume()
    {
        // given
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);

        // when
        await client.PauseAsync(AbortToken);
        await client.ResumeAsync(AbortToken);
        await client.ResumeAsync(AbortToken); // second resume is no-op

        // then — no exception
    }

    [Fact]
    public async Task PauseAsync_is_noop_after_disposal()
    {
        // given
        var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);
        await client.DisposeAsync();

        // when — should not throw
        await client.PauseAsync(AbortToken);
    }

    [Fact]
    public async Task ResumeAsync_is_noop_after_disposal()
    {
        // given
        var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);
        await client.DisposeAsync();

        // when — should not throw
        await client.ResumeAsync(AbortToken);
    }

    [Fact]
    public async Task PauseAsync_and_ResumeAsync_should_toggle_the_startup_gate_before_processing_starts()
    {
        // given
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);
        var gateField = typeof(AzureServiceBusConsumerClient).GetField(
            "_pauseGate",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;
        var gate = gateField.GetValue(client)!;
#pragma warning disable REFL009, REFL017 // REFL009/REFL017: refactor-safe nameof(ConsumerPauseGate.IsPaused) reflected over an object-typed private field; the member exists.
        var isPausedProp = gate.GetType()
            .GetProperty(nameof(ConsumerPauseGate.IsPaused), BindingFlags.Public | BindingFlags.Instance)!;
#pragma warning restore REFL009, REFL017

        // then - gate starts open
        ((bool)isPausedProp.GetValue(gate)!).Should().BeFalse();

        // when
        await client.PauseAsync(AbortToken);

        // then - startup gate closes while paused
        ((bool)isPausedProp.GetValue(gate)!)
            .Should()
            .BeTrue();

        // when
        await client.ResumeAsync(AbortToken);

        // then - gate reopens for late-starting listeners
        ((bool)isPausedProp.GetValue(gate)!)
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task ResumeAsync_should_not_mark_processing_as_started_before_listening_runs()
    {
        // given
        await using var client = new AzureServiceBusConsumerClient(_logger, "test-sub", 1, _options, _serviceProvider);
        var startedField = typeof(AzureServiceBusConsumerClient).GetField(
            "_hasStartedProcessing",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;

        await client.PauseAsync(AbortToken);

        // when
        await client.ResumeAsync(AbortToken);

        // then - resume only opens the gate before first ListeningAsync startup
        ((int)startedField.GetValue(client)!)
            .Should()
            .Be(0);
    }
}
