// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Configuration;

public sealed class DefaultDeliveryModeTests : TestBase
{
    [Theory]
    [InlineData(MessageLane.Bus, DeliveryMode.Auto)]
    [InlineData(MessageLane.Queue, DeliveryMode.Auto)]
    [InlineData(MessageLane.Bus, DeliveryMode.Durable)]
    [InlineData(MessageLane.Queue, DeliveryMode.Durable)]
    [InlineData(MessageLane.Bus, DeliveryMode.Direct)]
    [InlineData(MessageLane.Queue, DeliveryMode.Direct)]
    public async Task should_inherit_global_mode_in_all_ordinary_call_forms(MessageLane lane, DeliveryMode mode)
    {
        await using var provider = _CreateProvider(mode);
        provider.GetRequiredService<IOptions<MessagingOptions>>().Value.DefaultDeliveryMode.Should().Be(mode);
        var message = new TestMessage("inherit");
        if (lane == MessageLane.Bus)
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.PublishAsync(message, AbortToken);
            await bus.PublishAsync(message, options: null, AbortToken);
            await bus.PublishAsync(message, new PublishOptions { CorrelationId = "record" }, AbortToken);
            await bus.PublishAsync(message, static options => options.WithCorrelationId("fluent"), AbortToken);
        }
        else
        {
            var queue = provider.GetRequiredService<IQueue>();
            await queue.EnqueueAsync(message, AbortToken);
            await queue.EnqueueAsync(message, options: null, AbortToken);
            await queue.EnqueueAsync(message, new QueueOptions { CorrelationId = "record" }, AbortToken);
            await queue.EnqueueAsync(message, static options => options.WithCorrelationId("fluent"), AbortToken);
        }

        await _AssertStoredCountAsync(provider, lane, mode == DeliveryMode.Durable ? 4 : 0);
    }

    [Theory]
    [InlineData(MessageLane.Bus, DeliveryMode.Durable, DeliveryMode.Auto)]
    [InlineData(MessageLane.Queue, DeliveryMode.Durable, DeliveryMode.Auto)]
    [InlineData(MessageLane.Bus, DeliveryMode.Durable, DeliveryMode.Direct)]
    [InlineData(MessageLane.Queue, DeliveryMode.Durable, DeliveryMode.Direct)]
    [InlineData(MessageLane.Bus, DeliveryMode.Direct, DeliveryMode.Durable)]
    [InlineData(MessageLane.Queue, DeliveryMode.Direct, DeliveryMode.Durable)]
    public async Task should_prioritize_explicit_mode_over_global_mode(
        MessageLane lane,
        DeliveryMode globalMode,
        DeliveryMode explicitMode
    )
    {
        await using var provider = _CreateProvider(globalMode);
        var message = new TestMessage("override");
        if (lane == MessageLane.Bus)
        {
            await provider
                .GetRequiredService<IBus>()
                .PublishAsync(message, new PublishOptions { DeliveryMode = explicitMode }, AbortToken);
        }
        else
        {
            await provider
                .GetRequiredService<IQueue>()
                .EnqueueAsync(message, new QueueOptions { DeliveryMode = explicitMode }, AbortToken);
        }

        await _AssertStoredCountAsync(provider, lane, explicitMode == DeliveryMode.Durable ? 1 : 0);
    }

    [Fact]
    public async Task should_reject_invalid_global_mode_through_options_validation()
    {
        await using var provider = _CreateProvider((DeliveryMode)99);

        var resolve = () => provider.GetRequiredService<IOptions<MessagingOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>().WithMessage("*Default Delivery Mode*");
    }

    private static ServiceProvider _CreateProvider(DeliveryMode mode)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
            setup.Options.DefaultDeliveryMode = mode;
            setup.Bus.ForMessage<TestMessage>(message => message.Contract("test.default-mode"));
            setup.Queue.ForMessage<TestMessage>(message => message.Contract("test.default-mode"));
        });
        return services.BuildServiceProvider();
    }

    private static async Task _AssertStoredCountAsync(IServiceProvider provider, MessageLane lane, int expected)
    {
        var messages = await provider
            .GetRequiredService<IDataStorage>()
            .GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Publish,
                    Lane = lane,
                    PageSize = 10,
                },
                AbortToken
            );
        messages.TotalItems.Should().Be(expected);
    }

    private sealed record TestMessage(string Value);
}
