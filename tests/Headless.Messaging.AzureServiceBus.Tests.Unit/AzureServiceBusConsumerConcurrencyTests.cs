// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// The consumer's concurrency sizes the Azure processor, and the transport's prefetch reaches it, on both lanes and with
/// and without sessions.
/// </summary>
public sealed class AzureServiceBusConsumerConcurrencyTests : TestBase
{
    private const string _ConnectionString =
        "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey";

    private readonly ServiceBusClient _serviceBusClient = Substitute.For<ServiceBusClient>();
    private readonly IAzureServiceBusClientPool _pool = Substitute.For<IAzureServiceBusClientPool>();
    private ServiceBusProcessorOptions? _processorOptions;
    private ServiceBusSessionProcessorOptions? _sessionOptions;

    public AzureServiceBusConsumerConcurrencyTests()
    {
        _serviceBusClient
            .CreateProcessor(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ServiceBusProcessorOptions>())
            .Returns(call =>
            {
                _processorOptions = call.Arg<ServiceBusProcessorOptions>();
                return Substitute.For<ServiceBusProcessor>();
            });
        _serviceBusClient
            .CreateProcessor(Arg.Any<string>(), Arg.Any<ServiceBusProcessorOptions>())
            .Returns(call =>
            {
                _processorOptions = call.Arg<ServiceBusProcessorOptions>();
                return Substitute.For<ServiceBusProcessor>();
            });
        _serviceBusClient
            .CreateSessionProcessor(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ServiceBusSessionProcessorOptions>())
            .Returns(call =>
            {
                _sessionOptions = call.Arg<ServiceBusSessionProcessorOptions>();
                return Substitute.For<ServiceBusSessionProcessor>();
            });
        _serviceBusClient
            .CreateSessionProcessor(Arg.Any<string>(), Arg.Any<ServiceBusSessionProcessorOptions>())
            .Returns(call =>
            {
                _sessionOptions = call.Arg<ServiceBusSessionProcessorOptions>();
                return Substitute.For<ServiceBusSessionProcessor>();
            });
        _pool.GetClient().Returns(_serviceBusClient);
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_size_the_processor_from_the_consumer_concurrency(MessageLane lane)
    {
        // given
        await using var client = _CreateClient(concurrency: 6, lane, options => options.PrefetchCount = 12);

        // when
        await _OpenAsync(client, lane);

        // then
        _processorOptions.Should().NotBeNull();
        _processorOptions!.MaxConcurrentCalls.Should().Be(6);
        _processorOptions.PrefetchCount.Should().Be(12);
        _processorOptions.AutoCompleteMessages.Should().BeFalse();
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_spend_the_consumer_concurrency_on_sessions_one_message_each(MessageLane lane)
    {
        // given
        await using var client = _CreateClient(
            concurrency: 4,
            lane,
            options =>
            {
                options.EnableSessions = true;
                options.PrefetchCount = 3;
            }
        );

        // when
        await _OpenAsync(client, lane);

        // then
        _sessionOptions.Should().NotBeNull();
        _sessionOptions!.MaxConcurrentSessions.Should().Be(4);
        _sessionOptions.MaxConcurrentCallsPerSession.Should().Be(1);
        _sessionOptions.PrefetchCount.Should().Be(3);
        _sessionOptions.AutoCompleteMessages.Should().BeFalse();
    }

    [Fact]
    public async Task should_run_one_call_at_a_time_when_a_caller_outside_the_core_asks_for_no_concurrency()
    {
        // given
        await using var client = _CreateClient(concurrency: 0, MessageLane.Bus);

        // when
        await _OpenAsync(client, MessageLane.Bus);

        // then
        _processorOptions!.MaxConcurrentCalls.Should().Be(1);
    }

    private AzureServiceBusConsumerClient _CreateClient(
        byte concurrency,
        MessageLane lane,
        Action<AzureServiceBusMessagingOptions>? configure = null
    )
    {
        // AutoProvision off keeps the administration client out: these tests only read the processor options.
        var options = new AzureServiceBusMessagingOptions
        {
            ConnectionString = _ConnectionString,
            AutoProvision = false,
        };
        configure?.Invoke(options);

        return new AzureServiceBusConsumerClient(
            NullLogger.Instance,
            lane == MessageLane.Queue ? "orders" : "orders.projection",
            concurrency,
            Options.Create(options),
            new ServiceCollection().BuildServiceProvider(),
            _pool,
            lane
        );
    }

    private static async Task _OpenAsync(AzureServiceBusConsumerClient client, MessageLane lane)
    {
        if (lane == MessageLane.Queue)
        {
            await client.SubscribeAsync(["orders"], AbortToken);
            return;
        }

        await client.ConnectAsync(AbortToken);
    }
}
